using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Games;
using KroModIx.Services.Steam;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Lädt Plugin-Assemblies und initialisiert die <see cref="IGameModPlugin"/>-Instanzen.
/// <c>Assembly.LoadFrom</c> ohne <c>AssemblyLoadContext</c> (Checkmk-Erkenntnis:
/// LoadContext verursacht InvalidCastException-Kaskaden bei Avalonia-Views).
/// Kaputte Plugins blockieren nie den App-Start (try/catch pro Plugin, Log.Warn).
///
/// Unterstützt zwei Modi:
/// <list type="bullet">
/// <item><see cref="ActivateManyAsync"/> — beim Start alle geplanten Plugins.</item>
/// <item><see cref="ActivateOneAsync"/> — zur Laufzeit ein einzelnes frisch
/// installiertes Plugin (M4 „Install-Karte, kein Restart nötig").</item>
/// </list>
/// </summary>
public sealed class PluginActivator
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SteamLibraryService _steam;
    private readonly ISecretProtection _secrets;
    private readonly IDialogService _dialogs;
    private readonly INotificationSink _notifications;
    private readonly ILocalization _localization;
    private readonly IHostShell _shell;
    private readonly IAiService _ai;
    private readonly INexusService _nexus;
    private readonly IWorkshopService _workshop;
    private readonly StatusProgressCoordinator _progress;
    private readonly ManualGamesService _manualGames;

    /// <summary>Nach der App-Composition gesetzt (kein Ctor-Param, weil
    /// BadgeService selbst einen PluginActivator braucht → Circular-Dep).
    /// Wird beim <see cref="HostServicesImpl"/>-Erzeugen an das Plugin
    /// weitergegeben, damit <c>IHostServices.RequestUpdateBadgeRefreshAsync</c>
    /// den BadgeService triggern kann.</summary>
    public GameUpdateBadgeService? UpdateBadges { get; set; }

    private readonly List<LoadedPlugin> _loaded = new();
    private readonly object _lock = new();

    public PluginActivator(
        SteamLibraryService steam,
        ISecretProtection secrets,
        IDialogService dialogs,
        INotificationSink notifications,
        ILocalization localization,
        IHostShell shell,
        IAiService ai,
        INexusService nexus,
        IWorkshopService workshop,
        StatusProgressCoordinator progress,
        ManualGamesService manualGames)
    {
        _steam = steam;
        _secrets = secrets;
        _dialogs = dialogs;
        _notifications = notifications;
        _localization = localization;
        _shell = shell;
        _ai = ai;
        _nexus = nexus;
        _workshop = workshop;
        _progress = progress;
        _manualGames = manualGames;
    }

    /// <summary>Aktuell geladene Plugins (Thread-safe Snapshot).</summary>
    public IReadOnlyList<LoadedPlugin> Loaded
    {
        get { lock (_lock) return _loaded.ToList(); }
    }

    /// <summary>Feuert, wenn ein neues Plugin geladen ODER ein bestehendes entladen wurde.</summary>
    public event EventHandler? LoadedChanged;

    public async Task<IReadOnlyList<LoadedPlugin>> ActivateManyAsync(
        IReadOnlyList<PluginActivationDecision> decisions,
        CancellationToken ct = default)
    {
        var results = new List<LoadedPlugin>();
        foreach (var d in decisions.Where(d => d.Activate))
        {
            var loaded = await ActivateOneAsync(d, ct).ConfigureAwait(false);
            if (loaded is not null) results.Add(loaded);
        }
        return results;
    }

    public async Task<LoadedPlugin?> ActivateOneAsync(
        PluginActivationDecision decision,
        CancellationToken ct = default)
    {
        var plugin = decision.Plugin;
        var manifest = plugin.Manifest;

        // Prüfung: schon geladen?
        lock (_lock)
        {
            if (_loaded.Any(l => l.Manifest.Id == manifest.Id))
            {
                Log.Warn("Plugin {Id} ist bereits geladen — Aktivierung übersprungen (Restart nötig für Update)",
                    manifest.Id);
                return null;
            }
        }

        try
        {
            var assembly = Assembly.LoadFrom(plugin.AssemblyPath);
            var entryType = assembly.GetType(manifest.EntryType, throwOnError: false);
            if (entryType is null)
            {
                Log.Warn("Plugin {Id}: Entry-Type {Type} nicht in Assembly {Asm}",
                    manifest.Id, manifest.EntryType, plugin.AssemblyPath);
                return null;
            }
            if (!typeof(IGameModPlugin).IsAssignableFrom(entryType))
            {
                Log.Warn("Plugin {Id}: Entry-Type {Type} implementiert IGameModPlugin nicht",
                    manifest.Id, manifest.EntryType);
                return null;
            }

            var instance = (IGameModPlugin)Activator.CreateInstance(entryType)!;
            var host = new HostServicesImpl(
                manifest.Id, _secrets, _dialogs, _notifications, _localization, _shell, _ai,
                _nexus, title => _progress.Begin(title), _manualGames, UpdateBadges, _workshop);

            var detectedGames = BuildDetectedGames(decision);
            await instance.InitializeAsync(host, detectedGames, ct).ConfigureAwait(false);

            var loaded = new LoadedPlugin(manifest, instance, host, detectedGames);
            lock (_lock) _loaded.Add(loaded);
            LoadedChanged?.Invoke(this, EventArgs.Empty);

            Log.Info("Plugin geladen: {Id} v{Ver} ({Games} Spiel(e))",
                manifest.Id, manifest.Version, detectedGames.Count);
            // Debug-Diagnose fuer Plugin-vs-Sidebar-Match-Bugs (v1.15.1+):
            // in DEBUG-Level damit produktive Logs nicht ueberlaufen, aber bei
            // Bugreport „Plugin nicht sichtbar fuer Spiel X" sofort verfuegbar.
            if (detectedGames.Count > 0)
                Log.Debug("  DetectedGames InstallDirs: {Dirs}",
                    string.Join(" | ", detectedGames.Select(d => d.InstallDir)));
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin {Id} konnte nicht aktiviert werden ({Asm}) — App läuft weiter",
                manifest.Id, plugin.AssemblyPath);
            return null;
        }
    }

    /// <summary>v1.16.0: propagiert ein zur Laufzeit hinzugefuegtes Manual-Game
    /// an alle geladenen Plugins, deren <see cref="GameTarget"/>s matched.
    /// Baut das <see cref="DetectedGame"/> nach demselben Muster wie
    /// <see cref="BuildDetectedGames"/> auf, fuegt es der LoadedPlugin-Liste
    /// hinzu (damit spaeteres <c>GetTabContributions</c> das Game findet) und
    /// ruft <c>IGameModPlugin.OnGameAddedAsync</c>. Ohne diesen Hook muss der
    /// User nach jedem Manual-Add die App neu starten damit Plugins reagieren.</summary>
    public async Task NotifyGameAddedAsync(DiscoveredGame game, CancellationToken ct = default)
    {
        LoadedPlugin[] snap;
        lock (_lock) snap = _loaded.ToArray();
        foreach (var loaded in snap)
        {
            var detected = TryBuildDetectedGameFor(loaded.Manifest, game);
            if (detected is null) continue;
            // LoadedPlugin.DetectedGames intern ist ein List<DetectedGame>
            // (siehe BuildDetectedGames) — Cast fuers Inplace-Append.
            if (loaded.DetectedGames is List<DetectedGame> list
                && !list.Any(dg => string.Equals(dg.InstallDir, detected.InstallDir, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(detected);
            }
            try
            {
                await loaded.Plugin.OnGameAddedAsync(detected, ct).ConfigureAwait(false);
                Log.Info("Plugin {Id}: OnGameAddedAsync fuer {Game} aufgerufen",
                    loaded.Manifest.Id, detected.InstallDir);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Plugin {Id}.OnGameAddedAsync warf", loaded.Manifest.Id);
            }
        }
        LoadedChanged?.Invoke(this, EventArgs.Empty);
    }

    private DetectedGame? TryBuildDetectedGameFor(PluginManifest manifest, DiscoveredGame game)
    {
        GameTarget? target = null;
        if (game.SteamAppId is int appId)
            target = manifest.Targets.FirstOrDefault(t => t.SteamAppId == appId);
        if (target is null && !string.IsNullOrWhiteSpace(game.Engine))
            target = manifest.Targets.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.Engine)
                && string.Equals(t.Engine, game.Engine, StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;

        string? userDataDir = null;
        string? protonPrefix = null;
        RuntimeKind runtime = OperatingSystem.IsWindows() ? RuntimeKind.Native : RuntimeKind.Proton;
        if (game.SteamAppId is int steamId && OperatingSystem.IsLinux())
        {
            protonPrefix = _steam.FindProtonPrefix(steamId);
            userDataDir = _steam.FindProtonUserDocumentsDir(steamId);
            if (protonPrefix is null) runtime = RuntimeKind.Native;
        }
        return new DetectedGame(
            Target: target,
            InstallDir: game.InstallDir,
            UserDataDir: userDataDir,
            ProtonPrefix: protonPrefix,
            Runtime: runtime,
            Source: game.Source == DiscoveredGameSource.Steam ? GameSource.Steam : GameSource.Manual);
    }

    public async Task ShutdownAllAsync()
    {
        LoadedPlugin[] snap;
        lock (_lock) { snap = _loaded.ToArray(); _loaded.Clear(); }
        foreach (var p in snap)
        {
            try { await p.Plugin.ShutdownAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warn(ex, "ShutdownAsync von {Id} warf", p.Manifest.Id); }
        }
        LoadedChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<DetectedGame> BuildDetectedGames(PluginActivationDecision decision)
    {
        var result = new List<DetectedGame>();
        foreach (var game in decision.MatchedGames)
        {
            var target = decision.Plugin.Manifest.Targets
                .FirstOrDefault(t => t.SteamAppId == game.SteamAppId);
            if (target is null) continue;

            string? userDataDir = null;
            string? protonPrefix = null;
            RuntimeKind runtime = OperatingSystem.IsWindows() ? RuntimeKind.Native : RuntimeKind.Proton;
            if (game.SteamAppId is int appId && OperatingSystem.IsLinux())
            {
                protonPrefix = _steam.FindProtonPrefix(appId);
                userDataDir = _steam.FindProtonUserDocumentsDir(appId);
                if (protonPrefix is null) runtime = RuntimeKind.Native;
            }

            result.Add(new DetectedGame(
                Target: target,
                InstallDir: game.InstallDir,
                UserDataDir: userDataDir,
                ProtonPrefix: protonPrefix,
                Runtime: runtime,
                Source: game.Source == DiscoveredGameSource.Steam ? GameSource.Steam : GameSource.Manual));
        }
        return result;
    }
}

/// <summary>Ein geladenes, initialisiertes Plugin — nicht mehr aus dem Prozess
/// entfernbar (kein AssemblyLoadContext, siehe M4-Plan). Speichert alle
/// Instanzen die das Host-VM zum Rendern der Tabs braucht.</summary>
public sealed record LoadedPlugin(
    PluginManifest Manifest,
    IGameModPlugin Plugin,
    IHostServices Host,
    IReadOnlyList<DetectedGame> DetectedGames);
