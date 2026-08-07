using System;
using System.Diagnostics;
using ModManager.PluginContracts;
using NLog;

namespace ModManager.Services.Plugins;

/// <summary>Implementiert <see cref="IHostShell"/>. Navigation ist ein Event,
/// das das MainWindow-VM abonniert.</summary>
public sealed class HostShellImpl : IHostShell
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;

    public void OpenExternalUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte URL nicht öffnen: {Url}", url);
        }
    }

    public void OpenDirectory(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Verzeichnis nicht öffnen: {Path}", path);
        }
    }

    public void RequestNavigation(string gameId, string? tabId = null)
        => NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(gameId, tabId));
}

public sealed record NavigationRequestedEventArgs(string GameId, string? TabId);
