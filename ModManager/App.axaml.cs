using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ModManager.Localization;
using ModManager.PluginContracts;
using ModManager.Services;
using ModManager.Services.Ai;
using ModManager.Services.Games;
using ModManager.Services.Plugins;
using ModManager.Services.Steam;
using ModManager.ViewModels;
using ModManager.Views;
using NLog;

namespace ModManager;

public partial class App : Application
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public IServiceProvider Services { get; private set; } = null!;

    // GC-Referenz halten, sonst wird der TrayController eingesammelt und das
    // Tray-Icon verschwindet nach dem ersten Minimieren.
    private TrayController? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServiceProvider();

        var settings = Services.GetRequiredService<AppSettingsService>();
        LocalizationService.Instance.SetCulture(settings.Current.UiCulture);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow { DataContext = mainVm };

            _tray = new TrayController(this, mainWindow);
            _tray.Install();

            desktop.MainWindow = mainWindow;

            // Discovery + Plugin-Activation asynchron im Hintergrund starten;
            // MainWindow steht schon während der Discovery.
            mainWindow.Opened += (_, _) => _ = mainVm.InitializeAsync();

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

        // Utility
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<ISecretProtection, SecretProtection>();
        services.AddSingleton<HostUpdateService>();

        // Games-Discovery
        services.AddSingleton<SteamLibraryService>();
        services.AddSingleton<GameCoverService>();
        services.AddSingleton<ManualGamesService>();
        services.AddSingleton<GameDiscoveryService>();
        services.AddSingleton<GamesCacheService>();

        // Plugin-System — Host-Impls der Plugin-Contracts
        services.AddSingleton<HostShellImpl>();
        services.AddSingleton<IHostShell>(sp => sp.GetRequiredService<HostShellImpl>());
        services.AddSingleton<ILocalization, LocalizationBridge>();
        services.AddSingleton<IDialogService, DialogServiceImpl>();
        services.AddSingleton<NotificationSinkImpl>();
        services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<NotificationSinkImpl>());
        services.AddSingleton<StatusProgressCoordinator>();
        services.AddSingleton<PluginRegistryScanner>();
        services.AddSingleton<PluginActivationPlanner>();
        services.AddSingleton<PluginActivator>();
        services.AddSingleton<PluginIndexService>();
        services.AddSingleton<PluginInstaller>();
        services.AddSingleton<PluginUpdateService>();

        // KI — Kroste-Baukasten. AiSettingsService persistiert Provider/
        // Endpoint/Modell/verschlüsselte API-Keys. HostAiServiceImpl wrappt
        // AiProviderFactory für die Plugin-Contract-API IAiService.
        services.AddHttpClient();
        services.AddSingleton<AiSettingsService>();
        services.AddSingleton<AiProviderFactory>();
        services.AddSingleton<IAiService, HostAiServiceImpl>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
