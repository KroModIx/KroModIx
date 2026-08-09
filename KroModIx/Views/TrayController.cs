using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NLog;

namespace KroModIx.Views;

/// <summary>
/// System-Tray-Integration nach Kroste-Standard.
/// Minimieren → Hide (Fenster verschwindet in den Tray). Schließen → normal exit.
/// Vier Pflicht-Absicherungen: GC-Referenz beim Aufrufer, Restore-Guard, try/catch mit
/// Fallback (headless-Server / kaputtes DBus), Linux zieht Tmds.DBus.Protocol transitiv.
/// </summary>
public sealed class TrayController
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Application _app;
    private readonly Window _window;
    private TrayIcon? _tray;
    private bool _restoreInProgress;

    public TrayController(Application app, Window window)
    {
        _app = app;
        _window = window;
    }

    public void Install()
    {
        try
        {
            var iconUri = new Uri("avares://KroModIx/Assets/kromodix.png");
            var icon = AssetLoader.Exists(iconUri)
                ? new WindowIcon(new Bitmap(AssetLoader.Open(iconUri)))
                : null;

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "KroModIx",
                IsVisible = true,
                Menu = BuildMenu(),
            };
            _tray.Clicked += (_, _) => Restore();

            TrayIcon.SetIcons(_app, new TrayIcons { _tray });
            _window.PropertyChanged += OnWindowPropertyChanged;

            Log.Info("System-Tray installiert (Minimize → Tray).");
        }
        catch (Exception ex)
        {
            _tray = null;
            Log.Warn(ex, "System-Tray nicht verfügbar — Fallback: Standard-Minimieren.");
        }
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show");
        showItem.Click += (_, _) => Restore();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();
        menu.Add(quitItem);

        return menu;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty) return;
        if (_restoreInProgress) return;
        if (_window.WindowState != WindowState.Minimized) return;
        _window.Hide();
    }

    private void Restore()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _restoreInProgress = true;
            try
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            }
            finally
            {
                _restoreInProgress = false;
            }
        });
    }

    private void Quit()
    {
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
