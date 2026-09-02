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
        // v1.24.1: analog UpdateBadges — Conflicts-Scanner braucht selbst
        // den PluginActivator (Loaded-Liste), sonst Circular-Dep.
        Services.GetRequiredService<PluginActivator>().Conflicts =
            Services.GetRequiredService<IConflictScanner>();

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
                _ = AutoValidateNexusAsync();
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
        // v1.28.1: zieht fehlende Plugins zu erkannten Spielen automatisch nach.
        services.AddSingleton<PluginAutoInstallService>();
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

        // v1.17: Zentraler Steam-Workshop-Baukasten (Discovery + optional
        // Web-API-Enrichment). Plugins (LS25, Icarus, Satisfactory) muessen
        // die Pfad-Discovery nicht mehr selbst machen.
        services.AddSingleton<IWorkshopService>(sp =>
        {
            var library = sp.GetRequiredService<KroModIx.Services.Steam.SteamLibraryService>();
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var http = httpFactory.CreateClient("workshop");
            return new KroModIx.Services.Steam.HostWorkshopServiceImpl(library, http);
        });

        // v1.18: Zentraler Bild-Decoder — nimmt beliebige Bild-Bytes
        // (WebP/AVIF/DDS/PNG/JPEG/...) und liefert Avalonia-Bitmap.
        // ffmpeg-Convert-Fallback fuer non-native Formate.
        services.AddSingleton<IImageDecoder, KroModIx.Services.Images.HostImageDecoderImpl>();

        // v1.20: Zentraler Beschreibungs-Parser fuer Mod-Descriptions
        // (HTML+BBCode). Alle Nexus-Plugins nutzen ihn statt eigener
        // Regex-Chains — Bug-Fixes an einer Stelle greifen ueberall.
        services.AddSingleton<IDescriptionParser, KroModIx.Services.Text.HostDescriptionParserImpl>();

        // v1.23.0: Backup-Baukasten — Snapshot vor Install, Restore vom User
        // via Host-UI. Singleton weil er Cross-Plugin und Cross-Spiel arbeitet.
        services.AddSingleton<IBackupService, KroModIx.Services.Backup.HostBackupServiceImpl>();

        // v1.24.0: Konflikt-Scanner — findet Files die von mehr als einem Mod
        // beansprucht werden. Plugins implementieren IConflictSource optional.
        services.AddSingleton<IConflictScanner, KroModIx.Services.Conflicts.HostConflictScannerImpl>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<PluginUpdatesViewModel>();
        services.AddTransient<PluginHealthViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>Loest beim App-Start den Nexus-API-Key-Check aus wenn ein Key
    /// im Store liegt. Ohne diesen Auto-Validate steht <c>HostNexusServiceImpl.IsPremium</c>
    /// bis zum ersten Settings-Fenster-Open auf false — Plugins geben dann
    /// den Download-Button auch fuer Premium-User disabled aus, obwohl der
    /// Key laengst validiert ist. Fehler werden verschluckt (Netzausfall,
    /// Nexus-Downtime) — der Manuelle-Validieren-Button im Settings-Fenster
    /// bleibt als Fallback.</summary>
    private async Task AutoValidateNexusAsync()
    {
        try
        {
            var nexus = Services!.GetRequiredService<KroModIx.Services.Nexus.HostNexusServiceImpl>();
            if (!nexus.HasApiKey) return;
            var result = await nexus.ValidateAsync();
            Log.Info("Nexus-Auto-Validate: {Msg}", result.Message);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Nexus-Auto-Validate fehlgeschlagen (unkritisch).");
        }
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
