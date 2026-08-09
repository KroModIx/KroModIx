using System;
using System.Diagnostics;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>Implementiert <see cref="IHostShell"/>. Navigation ist ein Event,
/// das das MainWindow-VM abonniert.</summary>
public sealed class HostShellImpl : IHostShell
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;

    public void OpenExternalUrl(string url)
    {
        Log.Info("OpenExternalUrl: {Url}", url);
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                StartLinuxOpen("xdg-open", url);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte URL nicht öffnen: {Url}", url);
        }
    }

    public void OpenDirectory(string path)
    {
        Log.Info("OpenDirectory: {Path}", path);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
            else if (OperatingSystem.IsLinux())
            {
                StartLinuxOpen("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Verzeichnis nicht öffnen: {Path}", path);
        }
    }

    /// <summary>Startet ein Linux-Kommando mit einem Argument sicher — Path als
    /// <see cref="ProcessStartInfo.ArgumentList"/>, nicht als <c>Arguments</c>-
    /// String. Sonst würde ein Pfad wie
    /// „/pfx/drive_c/users/steamuser/My Documents/…" am ersten Space
    /// aufgesplittet an xdg-open übergeben und der Ordner wird nicht geöffnet.
    /// UseShellExecute=false damit .NET das ArgumentList-Quoting selbst
    /// übernimmt (mit true würde .NET auf Linux nur den Arguments-String
    /// verwenden und ArgumentList ignorieren).</summary>
    private static void StartLinuxOpen(string command, string arg)
    {
        var psi = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    public void RequestNavigation(string gameId, string? tabId = null)
        => NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(gameId, tabId));
}

public sealed record NavigationRequestedEventArgs(string GameId, string? TabId);
