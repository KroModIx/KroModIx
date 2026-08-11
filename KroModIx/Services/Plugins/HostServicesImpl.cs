using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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

    public HostServicesImpl(
        string pluginId,
        ISecretProtection secrets,
        IDialogService dialogs,
        INotificationSink notifications,
        ILocalization localization,
        IHostShell shell,
        IAiService ai,
        Func<string, IProgressScope> progressFactory,
        ManualGamesService? manualGames = null)
    {
        _pluginId = pluginId;
        Logger = LogManager.GetLogger($"Plugin.{pluginId}");
        Secrets = secrets;
        Dialogs = dialogs;
        Notifications = notifications;
        Localization = localization;
        Shell = shell;
        Ai = ai;
        _progressFactory = progressFactory;
        _manualGames = manualGames;

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

    public HttpClient CreateHttpClient(string? subsystem = null)
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
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

    public bool TrySetManualGameCover(string installDir, string coverPath)
    {
        if (_manualGames is null) return false;
        if (string.IsNullOrWhiteSpace(installDir) || string.IsNullOrWhiteSpace(coverPath))
            return false;
        var entry = _manualGames.All.FirstOrDefault(g =>
            string.Equals(g.InstallDir, installDir, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return false;
        if (string.Equals(entry.CoverPath, coverPath, StringComparison.Ordinal)) return true; // no-op
        _manualGames.Update(entry.Id, e => e.CoverPath = coverPath);
        Logger.Debug("Manual-Cover gesetzt: {Name} -> {Path}", entry.DisplayName, coverPath);
        ManualCoverChanged?.Invoke(null, entry.Id);
        return true;
    }
}
