using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using NLog;

namespace KroModIx.ViewModels;

/// <summary>VM für die „Plugin verfügbar → ⬇ Installieren"-Karte
/// im Content-Bereich des MainWindow.</summary>
public sealed partial class InstallCardViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PluginIndexEntry _entry;
    private readonly PluginInstaller _installer;
    private readonly PluginActivator _activator;
    private readonly PluginActivationPlanner _planner;
    private readonly Version _hostVersion;
    private readonly Func<IReadOnlyList<DiscoveredGame>> _gamesProvider;
    private readonly Func<Task> _onInstalledLive;

    public InstallCardViewModel(
        PluginIndexEntry entry,
        string gameDisplayName,
        PluginInstaller installer,
        PluginActivator activator,
        PluginActivationPlanner planner,
        Version hostVersion,
        Func<IReadOnlyList<DiscoveredGame>> gamesProvider,
        Func<Task> onInstalledLive)
    {
        _entry = entry;
        _installer = installer;
        _activator = activator;
        _planner = planner;
        _hostVersion = hostVersion;
        _gamesProvider = gamesProvider;
        _onInstalledLive = onInstalledLive;
        GameDisplayName = gameDisplayName;
    }

    public string GameDisplayName { get; }
    public string PluginTitle => _entry.DisplayName;
    public string PluginAuthor => _entry.Author;
    public string PluginDescription => _entry.Description;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    private bool CanInstall() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsBusy = true;
        StatusMessage = "Lade …";
        try
        {
            var discovered = await _installer.InstallLatestAsync(_entry).ConfigureAwait(true);
            if (discovered is null)
            {
                StatusMessage = "Download fehlgeschlagen — siehe Log.";
                return;
            }

            // Live-Aktivierung (ohne App-Restart). PlanSingle MUSS die aktuelle
            // Games-Liste bekommen, sonst wird das Plugin ohne DetectedGames
            // initialisiert und liefert später keine Tabs.
            var decision = _planner.PlanSingle(discovered, _gamesProvider(), _hostVersion);
            if (!decision.Activate)
            {
                StatusMessage = $"Plugin heruntergeladen, aber nicht aktivierbar ({decision.SkipReason}).";
                return;
            }
            var loaded = await _activator.ActivateOneAsync(decision).ConfigureAwait(true);
            if (loaded is null)
            {
                StatusMessage = "Plugin heruntergeladen, aber Aktivierung fehlgeschlagen — siehe Log.";
                return;
            }
            StatusMessage = "Installiert und aktiv.";
            await _onInstalledLive().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Install fehlgeschlagen für {Id}", _entry.Id);
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
