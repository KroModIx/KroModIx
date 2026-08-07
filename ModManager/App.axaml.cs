using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ModManager.Localization;
using ModManager.Services;
using ModManager.ViewModels;
using ModManager.Views;
using NLog;

namespace ModManager;

public partial class App : Application
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public IServiceProvider Services { get; private set; } = null!;

    // GC-Referenz auf TrayController halten (Kroste-Standard, sonst wird sie GC'd
    // und das Tray-Icon verschwindet nach dem ersten Minimieren).
    private TrayController? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServiceProvider();

        // Gespeicherte UI-Sprache anwenden BEVOR das erste Fenster gebaut wird,
        // sonst flackert der Wechsel beim App-Start.
        var settings = Services.GetRequiredService<AppSettingsService>();
        LocalizationService.Instance.SetCulture(settings.Current.UiCulture);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow { DataContext = mainVm };

            _tray = new TrayController(this, mainWindow);
            _tray.Install();

            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) =>
            {
                try { settings.Save(); }
                catch (Exception ex) { Log.Warn(ex, "Konnte Settings beim Exit nicht speichern."); }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Utility-Services
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<ISecretProtection, SecretProtection>();
        services.AddSingleton<HostUpdateService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
