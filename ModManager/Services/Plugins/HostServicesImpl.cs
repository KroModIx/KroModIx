using System;
using System.IO;
using System.Net;
using System.Net.Http;
using ModManager.Localization;
using ModManager.PluginContracts;
using NLog;

namespace ModManager.Services.Plugins;

/// <summary>
/// Implementierung von <see cref="IHostServices"/>, die einem konkreten
/// Plugin gegeben wird. Pro Plugin eine Instanz — <see cref="Logger"/>,
/// <see cref="PluginDataDir"/> und <see cref="PluginCacheDir"/> sind plugin-scoped.
/// </summary>
public sealed class HostServicesImpl : IHostServices
{
    private readonly string _pluginId;

    public HostServicesImpl(
        string pluginId,
        ISecretProtection secrets,
        IDialogService dialogs,
        INotificationSink notifications,
        ILocalization localization,
        IHostShell shell,
        Func<string, IProgressScope> progressFactory)
    {
        _pluginId = pluginId;
        Logger = LogManager.GetLogger($"Plugin.{pluginId}");
        Secrets = secrets;
        Dialogs = dialogs;
        Notifications = notifications;
        Localization = localization;
        Shell = shell;
        _progressFactory = progressFactory;

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

    public HttpClient CreateHttpClient(string? subsystem = null)
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var ua = string.IsNullOrEmpty(subsystem)
            ? $"ModManager-Plugin-{_pluginId}"
            : $"ModManager-Plugin-{_pluginId}/{subsystem}";
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
        return http;
    }

    public IProgressScope BeginProgress(string title) => _progressFactory(title);
}
