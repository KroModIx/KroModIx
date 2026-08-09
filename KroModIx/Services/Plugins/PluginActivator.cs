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
    private readonly StatusProgressCoordinator _progress;

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
        StatusProgressCoordinator progress)
    {
        _steam = steam;
        _secrets = secrets;
        _dialogs = dialogs;
        _notifications = notifications;
        _localization = localization;
        _shell = shell;
        _ai = ai;
        _progress = progress;
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
                title => _progress.Begin(title));

            var detectedGames = BuildDetectedGames(decision);
            await instance.InitializeAsync(host, detectedGames, ct).ConfigureAwait(false);

            var loaded = new LoadedPlugin(manifest, instance, host, detectedGames);
            lock (_lock) _loaded.Add(loaded);
            LoadedChanged?.Invoke(this, EventArgs.Empty);

            Log.Info("Plugin geladen: {Id} v{Ver} ({Games} Spiel(e))",
                manifest.Id, manifest.Version, detectedGames.Count);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin {Id} konnte nicht aktiviert werden ({Asm}) — App läuft weiter",
                manifest.Id, plugin.AssemblyPath);
            return null;
        }
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
