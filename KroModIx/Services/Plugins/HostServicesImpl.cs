using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using KroModIx.Localization;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Games;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Implementierung von <see cref="IHostServices"/>, die einem konkreten
/// Plugin gegeben wird. Pro Plugin eine Instanz — <see cref="Logger"/>,
/// <see cref="PluginDataDir"/> und <see cref="PluginCacheDir"/> sind plugin-scoped.
/// </summary>
public sealed class HostServicesImpl : IHostServices
{
    private readonly string _pluginId;

    private readonly ManualGamesService? _manualGames;
    private readonly GameUpdateBadgeService? _updateBadges;

    public HostServicesImpl(
        string pluginId,
        ISecretProtection secrets,
        IDialogService dialogs,
        INotificationSink notifications,
        ILocalization localization,
        IHostShell shell,
        IAiService ai,
        INexusService nexus,
        Func<string, IProgressScope> progressFactory,
        ManualGamesService? manualGames = null,
        GameUpdateBadgeService? updateBadges = null,
        IWorkshopService? workshop = null,
        IImageDecoder? images = null,
        IDescriptionParser? descriptions = null,
        IBackupService? backup = null,
        IConflictScanner? conflicts = null)
    {
        _pluginId = pluginId;
        Logger = LogManager.GetLogger($"Plugin.{pluginId}");
        Secrets = secrets;
        Dialogs = dialogs;
        Notifications = notifications;
        Localization = localization;
        Shell = shell;
        Ai = ai;
        Nexus = nexus;
        Workshop = workshop ?? NullWorkshopService.Instance;
        Images = images ?? NullImageDecoder.Instance;
        Descriptions = descriptions ?? NullDescriptionParser.Instance;
        Backup = backup ?? NullBackupService.Instance;
        Conflicts = conflicts ?? NullConflictScanner.Instance;
        _progressFactory = progressFactory;
        _manualGames = manualGames;
        _updateBadges = updateBadges;

        PluginDataDir = Path.Combine(AppPaths.ConfigRoot, "plugin-data", pluginId);
        Directory.CreateDirectory(PluginDataDir);
        PluginCacheDir = Path.Combine(AppPaths.CacheRoot, "plugin-cache", pluginId);
        Directory.CreateDirectory(PluginCacheDir);
    }

    private readonly Func<string, IProgressScope> _progressFactory;

    public Logger Logger { get; }
    public string PluginDataDir { get; }
    public string PluginCacheDir { get; }
    public ISecretProtection Secrets { get; }
    public IDialogService Dialogs { get; }
    public INotificationSink Notifications { get; }
    public ILocalization Localization { get; }
    public IHostShell Shell { get; }
    public IAiService Ai { get; }
    public INexusService Nexus { get; }
    public IWorkshopService Workshop { get; }
    public IImageDecoder Images { get; }
    public IDescriptionParser Descriptions { get; }
    public IBackupService Backup { get; }
    public IConflictScanner Conflicts { get; }

    public HttpClientHandler CreateHttpClientHandler(CookieContainer? cookies = null)
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        if (cookies is not null)
        {
            handler.CookieContainer = cookies;
            handler.UseCookies = true;
        }
        return handler;
    }

    public HttpClient CreateHttpClient(string? subsystem = null)
    {
        var handler = CreateHttpClientHandler();
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var ua = string.IsNullOrEmpty(subsystem)
            ? $"KroModIx-Plugin-{_pluginId}"
            : $"KroModIx-Plugin-{_pluginId}/{subsystem}";
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
        return http;
    }

    public IProgressScope BeginProgress(string title) => _progressFactory(title);

    /// <summary>Trigger, damit die MainWindow-Sidebar den neuen Cover-Path
    /// nachlädt. Wird von <see cref="TrySetManualGameCover"/> gefeuert;
    /// MainWindowViewModel horcht darauf und ruft LoadCoversAsync für die
    /// betroffene Kachel neu.</summary>
    public static event EventHandler<string>? ManualCoverChanged;

    /// <summary>Signalisiert, dass ein Manual-Game auf der Platte umbenannt
    /// oder verschoben wurde. Payload: (manualGameId, newInstallDir). Die
    /// MainWindow-Sidebar re-keyed daraufhin ihre in-memory Kachel-VM
    /// damit ein Klick den neuen Pfad öffnet. Contracts v1.10.3.</summary>
    public static event EventHandler<(string Id, string NewInstallDir)>? ManualGameRenamed;

    public async Task RequestUpdateBadgeRefreshAsync()
    {
        if (_updateBadges is null) return;
        try { await _updateBadges.RefreshAsync(); }
        catch (Exception ex) { Logger.Debug(ex, "RequestUpdateBadgeRefresh fehlgeschlagen"); }
    }

    public bool TryRenameManualGame(string oldInstallDir, string newInstallDir)
    {
        if (_manualGames is null) return false;
        if (string.IsNullOrWhiteSpace(oldInstallDir) || string.IsNullOrWhiteSpace(newInstallDir))
            return false;
        var entry = _manualGames.All.FirstOrDefault(g =>
            string.Equals(g.InstallDir, oldInstallDir, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Logger.Debug("TryRenameManualGame: kein Manual-Eintrag für {Old} gefunden", oldInstallDir);
            return false;
        }
        // Kollisionsschutz: existiert schon ein anderer Eintrag mit dem neuen
        // Pfad, würden wir zwei Kacheln auf denselben Ordner zeigen lassen.
        if (_manualGames.All.Any(g => g.Id != entry.Id
            && string.Equals(g.InstallDir, newInstallDir, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.Warn("TryRenameManualGame: {New} kollidiert mit bestehendem Manual-Eintrag — abgelehnt",
                newInstallDir);
            return false;
        }
        _manualGames.Update(entry.Id, e => e.InstallDir = newInstallDir);
        Logger.Info("Manual-Game re-keyed: {Name} ({Old} → {New})",
            entry.DisplayName, oldInstallDir, newInstallDir);
        ManualGameRenamed?.Invoke(null, (entry.Id, newInstallDir));
        return true;
    }

    public bool TrySetManualGameCover(string installDir, string coverPath)
    {
        if (_manualGames is null) return false;
        if (string.IsNullOrWhiteSpace(installDir) || string.IsNullOrWhiteSpace(coverPath))
            return false;
        var entry = _manualGames.All.FirstOrDefault(g =>
            string.Equals(g.InstallDir, installDir, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return false;
        // Kein no-op-Skip mehr: der Plugin schreibt oft den GLEICHEN Pfad
        // (z. B. `.renpyassist/sidebar-cover.png`) aber mit anderem Bild-
        // Inhalt (User hat den Ausschnitt neu gewählt). Ohne diesen Event
        // bleibt die Sidebar-Kachel auf dem alten Bitmap-Cache hängen.
        // Update immer + Event immer feuern; MainWindowViewModel lädt das
        // Bild aus der Datei neu (Bitmap-Ctor).
        bool pathChanged = !string.Equals(entry.CoverPath, coverPath, StringComparison.Ordinal);
        if (pathChanged)
            _manualGames.Update(entry.Id, e => e.CoverPath = coverPath);
        Logger.Debug("Manual-Cover gesetzt: {Name} -> {Path} (pathChanged={Changed})",
            entry.DisplayName, coverPath, pathChanged);
        ManualCoverChanged?.Invoke(null, entry.Id);
        return true;
    }
}
