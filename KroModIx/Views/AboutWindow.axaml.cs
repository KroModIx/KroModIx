using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    private UpdateCheckResult? _lastCheck;
    private bool _installInProgress;

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
        InstallUpdateButton.Click += OnInstallUpdate;
        OpenReleasePageButton.Click += (_, _) => OpenReleasePage();
        GithubButton.Click += (_, _) => Launch(GithubUrl);
        BmcButton.Click += (_, _) => Launch(BmcUrl);
        LogFolderButton.Click += (_, _) => OpenLogFolder();
    }

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        UpdateResult.Text = "…";
        UpdateInstallPanel.IsVisible = false;
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            _lastCheck = result;
            UpdateResult.Text = result.UpdateAvailable
                ? $"v{result.LatestVersion}"
                : result.LatestVersion is null
                    ? "?"
                    : "OK";

            if (result.UpdateAvailable)
                ShowInstallPanel(result);
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

    private void ShowInstallPanel(UpdateCheckResult result)
    {
        // Wenn ein passendes Asset fuer die aktuelle Plattform da ist, den
        // Install-Button aktivieren; sonst nur den Release-Seiten-Link.
        bool hasAsset = !string.IsNullOrEmpty(result.AssetUrl);
        UpdateAssetLabel.Text = hasAsset
            ? string.Format(L.T("About_UpdateReady"), result.LatestVersion, result.AssetName)
            : string.Format(L.T("About_UpdateNoAsset"), result.LatestVersion);
        InstallUpdateButton.IsEnabled = hasAsset;
        OpenReleasePageButton.IsVisible = !string.IsNullOrEmpty(result.ReleaseUrl);
        UpdateInstallPanel.IsVisible = true;
    }

    private async void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null || _installInProgress) return;
        if (_lastCheck is not { UpdateAvailable: true, AssetUrl: { Length: > 0 } assetUrl }
            || string.IsNullOrEmpty(_lastCheck.AssetName))
            return;

        // Confirm-Dialog vor dem Download — Kroste-Standard, keine Silent-Installs.
        var confirmed = await ShowConfirmDialogAsync(
            L.T("About_InstallUpdate"),
            string.Format(L.T("About_InstallConfirmMsg"),
                _lastCheck.LatestVersion, _lastCheck.AssetName));
        if (!confirmed) return;

        _installInProgress = true;
        InstallUpdateButton.IsEnabled = false;
        OpenReleasePageButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        UpdateProgressLabel.IsVisible = true;
        UpdateProgress.Value = 0;
        UpdateProgressLabel.Text = L.T("About_UpdateDownloading");

        try
        {
            var tmpPath = Path.Combine(Path.GetTempPath(),
                $"kromodix-update-{Guid.NewGuid():N}-{_lastCheck.AssetName}");
            var progress = new Progress<double>(f => Dispatcher.UIThread.Post(() =>
            {
                UpdateProgress.Value = f;
                UpdateProgressLabel.Text =
                    $"{L.T("About_UpdateDownloading")} {(int)(f * 100)}%";
            }));

            await _updateService.DownloadAssetAsync(assetUrl, tmpPath, progress);

            UpdateProgressLabel.Text = L.T("About_UpdateApplying");
            _updateService.ApplyUpdateAndRestart(tmpPath);

            // PFLICHT laut Skill: nach Start des Installer-Skripts die App
            // aktiv beenden. Das Skript wartet per Wait-Process/kill -0 auf
            // das Prozessende — beendet die App sich NICHT selbst, wartet
            // das Skript ewig und der Nutzer sieht "100%"-Hänger.
            TerminateForUpdate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Install fehlgeschlagen");
            UpdateProgressLabel.Text = L.T("About_UpdateFailed") + " " + ex.Message;
            InstallUpdateButton.IsEnabled = true;
            OpenReleasePageButton.IsEnabled = true;
            _installInProgress = false;
        }
    }

    /// <summary>Beendet die App-Instanz sofort — <see cref="Process.Kill()"/>
    /// als PRIMÄRER Weg (kein Environment.Exit, das würde blockierende
    /// Finalizer triggern, siehe kroste-avalonia references/autoupdate.md).
    /// Nach 1,5 s Fallback via Environment.Exit — falls Kill unerwartet
    /// haengt (headless-Corner-Case). Der Installer wartet nur auf das
    /// Verschwinden der PID, kein sauberes Ende noetig.</summary>
    private static void TerminateForUpdate()
    {
        Log.Info("Update: beende App fuer Installer-Uebernahme");
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            try { Environment.Exit(0); } catch { }
        });
        try { Process.GetCurrentProcess().Kill(); } catch { }
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var okBtn = new Button { Content = L.T("About_InstallUpdate"), Classes = { "accent" } };
        var cancelBtn = new Button { Content = L.T("Common_Cancel") };
        bool result = false;
        okBtn.Click += (_, _) => { result = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelBtn, okBtn },
                },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    private void OpenReleasePage()
    {
        if (_lastCheck?.ReleaseUrl is { Length: > 0 } url) Launch(url);
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
