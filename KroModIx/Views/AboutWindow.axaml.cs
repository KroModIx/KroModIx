using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KroModIx.Localization;
using KroModIx.Services;
using NLog;

namespace KroModIx.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GithubUrl = "https://github.com/KroModIx/KroModIx";
    private const string BmcUrl = "https://buymeacoffee.com/kroste";
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HostUpdateService? _updateService;

    // Parameterloser Ctor für den XAML-Designer.
    public AboutWindow()
    {
        InitializeComponent();
    }

    public AboutWindow(HostUpdateService updateService) : this()
    {
        _updateService = updateService;
        VersionText.Text = $"{L.T("About_Version")} {updateService.CurrentVersion}";
        UpdateButton.Click += OnCheckUpdate;
        GithubButton.Click += (_, _) => Launch(GithubUrl);
        BmcButton.Click += (_, _) => Launch(BmcUrl);
        LogFolderButton.Click += (_, _) => OpenLogFolder();
    }

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        UpdateResult.Text = "…";
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            UpdateResult.Text = result.UpdateAvailable
                ? $"v{result.LatestVersion}"
                : result.LatestVersion is null
                    ? "?"
                    : "OK";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Prüfung im About-Fenster fehlgeschlagen");
            UpdateResult.Text = "!";
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void Launch(string url)
    {
        try
        {
            TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Link konnte nicht geöffnet werden: {Url}", url);
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", logDir) { UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                Process.Start(new ProcessStartInfo("xdg-open", logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Log-Ordner konnte nicht geöffnet werden.");
        }
    }
}
