using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using KroModIx.Localization;
using KroModIx.Plugin.Contracts;
using KroModIx.Services;
using KroModIx.Services.Ai;
using KroModIx.Services.Api;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using KroModIx.Services.Steam;
using KroModIx.ViewModels;
using KroModIx.Views;
using NLog;

namespace KroModIx;

public partial class App : Application
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public IServiceProvider Services { get; private set; } = null!;

    // GC-Referenz halten, sonst wird der TrayController eingesammelt und das
    // Tray-Icon verschwindet nach dem ersten Minimieren.
    private TrayController? _tray;

    private ApiHost? _api;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServiceProvider();

        // Circular-Dep-Aufhebung: PluginActivator braucht Zugriff auf den
        // BadgeService (für IHostServices.RequestUpdateBadgeRefreshAsync),
        // aber BadgeService selbst braucht PluginActivator im Ctor. Setzen
        // nach der Composition.
        Services.GetRequiredService<PluginActivator>().UpdateBadges =
            Services.GetRequiredService<GameUpdateBadgeService>();

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
            mainWindow.Opened += (_, _) =>
            {
                _ = mainVm.InitializeAsync();
                _ = StartApiAsync(settings.Current);
                ScheduleAutoShutdownIfRequested(desktop);
            };

            desktop.Exit += (_, _) =>
            {
                try { settings.Save(); }
                catch (Exception ex) { Log.Warn(ex, "Konnte Settings beim Exit nicht speichern."); }
                try { _api?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); }
                catch (Exception ex) { Log.Warn(ex, "Konnte REST-API nicht sauber stoppen."); }
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
        services.AddSingleton<FolderEngineDetector>();
        services.AddSingleton<GameDiscoveryService>();
        services.AddSingleton<GamesCacheService>();
        services.AddSingleton<GameLauncherService>();

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
        services.AddSingleton<PluginUninstaller>();
        services.AddSingleton<PluginUpdateService>();
        services.AddSingleton<GameUpdateBadgeService>();
        // v1.12: Host-Profile-Export/Import (Multi-Host-Setup).
        services.AddSingleton<KroModIx.Services.HostProfile.HostProfileService>();

        // KI — Kroste-Baukasten. AiSettingsService persistiert Provider/
        // Endpoint/Modell/verschlüsselte API-Keys. HostAiServiceImpl wrappt
        // AiProviderFactory für die Plugin-Contract-API IAiService.
        services.AddHttpClient();
        services.AddSingleton<AiSettingsService>();
        services.AddSingleton<AiProviderFactory>();
        services.AddSingleton<IAiService, HostAiServiceImpl>();
        services.AddSingleton<SystemHardwareService>();

        // v1.14: Zentraler Nexus-Baukasten — API-Key + Client leben im Host,
        // alle Nexus-basierten Plugins (Icarus, Cyberpunk, …) teilen ihn.
        services.AddSingleton<KroModIx.Services.Nexus.NexusApiKeyStore>();
        services.AddSingleton<KroModIx.Services.Nexus.HostNexusServiceImpl>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var keyStore = sp.GetRequiredService<KroModIx.Services.Nexus.NexusApiKeyStore>();
            var impl = new KroModIx.Services.Nexus.HostNexusServiceImpl(httpFactory, keyStore);
            keyStore.SetNexus(impl); // Circular-Dep aufloesen
            return impl;
        });
        services.AddSingleton<INexusService>(sp =>
            sp.GetRequiredService<KroModIx.Services.Nexus.HostNexusServiceImpl>());

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<PluginUpdatesViewModel>();

        return services.BuildServiceProvider();
    }

    private async Task StartApiAsync(AppSettings settings)
    {
        try
        {
            var opts = ApiOptionsResolver.Resolve(settings, Program.LaunchOptions);
            _api = new ApiHost(Services);
            await _api.StartAsync(opts);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "REST-API-Start fehlgeschlagen.");
        }
    }

    /// <summary>Automatischer Shutdown der App nach <c>--auto-shutdown-after</c>.
    /// Gedacht für die Screenshot-driven Iteration — App startet, macht ihre
    /// Requests, geht wieder. Fires auf UI-Thread damit Avalonia sauber schließt.</summary>
    private void ScheduleAutoShutdownIfRequested(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var delay = Program.LaunchOptions.AutoShutdownAfter;
        if (delay is null || delay.Value <= TimeSpan.Zero) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay.Value);
            Log.Info("Auto-Shutdown nach {Seconds}s ausgelöst.", delay.Value.TotalSeconds);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try { desktop.Shutdown(0); }
                catch (Exception ex) { Log.Warn(ex, "Auto-Shutdown scheiterte."); }
            });
        });
    }
}
