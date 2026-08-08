using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModManager.PluginContracts;
using ModManager.Services;
using ModManager.Services.Games;
using ModManager.Services.Plugins;
using ModManager.Services.Steam;
using ModManager.Views;
using NLog;

namespace ModManager.ViewModels;

/// <summary>
/// Haupt-VM des MainWindow. Discovery bei Init + Filter/Sortierung der Sidebar
/// + Anzeige der Plugin-Tabs im Content-Bereich (aktuell selektiertes Spiel).
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _services;
    private readonly GameDiscoveryService _discovery;
    private readonly GamesCacheService _gamesCache;
    private readonly GameCoverService _covers;
    private readonly ManualGamesService _manual;
    private readonly PluginRegistryScanner _pluginScanner;
    private readonly PluginActivationPlanner _pluginPlanner;
    private readonly PluginActivator _pluginActivator;
    private readonly PluginIndexService _pluginIndex;
    private readonly PluginInstaller _pluginInstaller;
    private readonly PluginUpdateService _pluginUpdates;
    private readonly AppSettingsService _settings;
    private readonly HostUpdateService _hostUpdate;

    private PluginIndex? _indexCache;

    private readonly List<GameEntry> _allGames = new();

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
        _discovery = services.GetRequiredService<GameDiscoveryService>();
        _gamesCache = services.GetRequiredService<GamesCacheService>();
        _covers = services.GetRequiredService<GameCoverService>();
        _manual = services.GetRequiredService<ManualGamesService>();
        _pluginScanner = services.GetRequiredService<PluginRegistryScanner>();
        _pluginPlanner = services.GetRequiredService<PluginActivationPlanner>();
        _pluginActivator = services.GetRequiredService<PluginActivator>();
        _pluginIndex = services.GetRequiredService<PluginIndexService>();
        _pluginInstaller = services.GetRequiredService<PluginInstaller>();
        _pluginUpdates = services.GetRequiredService<PluginUpdateService>();
        _settings = services.GetRequiredService<AppSettingsService>();
        _hostUpdate = services.GetRequiredService<HostUpdateService>();

        _pluginActivator.LoadedChanged += (_, _) => Dispatcher.UIThread.Post(RefreshPluginStates);
        _pluginUpdates.UpdatesChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            AvailableUpdateCount = _pluginUpdates.AvailableUpdates.Count;
        });
    }

    public ObservableCollection<GameEntry> VisibleGames { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    private GameEntry? _selectedGame;

    public bool HasSelectedGame => SelectedGame is not null;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _onlyWithPlugin;

    [ObservableProperty]
    private ObservableCollection<TabItem>? _pluginTabs;

    [ObservableProperty]
    private InstallCardViewModel? _installCard;

    [ObservableProperty]
    private string _contentPlaceholderText = "Wähle links ein Spiel aus.";

    [ObservableProperty]
    private bool _showPluginTabs;

    [ObservableProperty]
    private bool _showInstallCard;

    [ObservableProperty]
    private bool _showContentPlaceholder = true;

    [ObservableProperty]
    private string _statusText = "Starte …";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdates))]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private int _availableUpdateCount;

    public bool HasAvailableUpdates => AvailableUpdateCount > 0;
    public string UpdateBadgeText => AvailableUpdateCount > 0 ? $"↑ {AvailableUpdateCount}" : "";

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();
    partial void OnOnlyWithPluginChanged(bool value) => ApplyFilterAndSort();

    // Wird während des App-Start-Bootstraps auf false gesetzt, damit die
    // vielen impliziten Selection-Wechsel (ListBox-Auto-Select nach Clear+Add,
    // Sortier-Refresh nach PluginIndex-Load) den persistierten
    // LastSelectedGameId nicht überschreiben, bevor RestoreLastSelection läuft.
    private bool _persistSelection;

    partial void OnSelectedGameChanged(GameEntry? value)
    {
        if (value is null)
        {
            PluginTabs = null;
            ShowPluginTabs = false;
            // Beim Filter-Wechsel („nur mit Plugin") ruft die ListBox
            // ItemsSource.Clear() → SelectedItem = null. Wenn der Filter
            // das gleiche Spiel gleich wieder selektiert, würde der
            // Render-Cache den erneuten Render als redundant abtun und
            // die Tabs blieben leer. Cache invalidieren.
            _lastRenderKey = null;
            return;
        }
        RenderContentForSelected(value);
        if (_persistSelection)
            _settings.Update(s => s.LastSelectedGameId = value.Key);
    }

    /// <summary>Kompletter Init-Ablauf beim App-Start:
    /// <list type="number">
    /// <item>Cache-Load — instant, Sidebar zeigt sofort die zuletzt bekannten Spiele</item>
    /// <item>Plugin-Discovery + Activation — läuft gegen die Cache-Games</item>
    /// <item>UI-Restore (LastSelectedGame)</item>
    /// <item>Fresh Discovery im Background — diff't neue/entfernte Spiele in die
    ///     Sidebar zurück und aktualisiert den Cache</item>
    /// </list>
    /// Beim ersten Start (leerer Cache) läuft Discovery synchron als Fallback.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // 1) Cache-Load — instant.
        var cached = _gamesCache.Load();
        if (cached.Count > 0)
        {
            _allGames.Clear();
            foreach (var g in cached) _allGames.Add(new GameEntry(g));
            Log.Info("Sidebar aus Cache: {Count} Spiele geladen", _allGames.Count);
            StatusText = $"{_allGames.Count} Spiele (Cache).";
        }
        else
        {
            // Erster App-Start: Discovery synchron, sonst startet die App
            // mit leerer Sidebar bis der Background-Job fertig ist.
            StatusText = "Discovery …";
            var initial = await Task.Run(() => _discovery.Discover(), ct).ConfigureAwait(true);
            _allGames.Clear();
            foreach (var g in initial) _allGames.Add(new GameEntry(g));
            _gamesCache.Save(initial);
            Log.Info("Sidebar (Erst-Discovery): {Count} Spiele geladen", _allGames.Count);
            StatusText = $"{_allGames.Count} Spiele erkannt.";
        }

        // 2) Cover parallel im Background laden (limitiert).
        _ = LoadCoversAsync(_allGames.ToArray(), ct);

        // 3) Plugin-Discovery + Activation gegen die Cache-Games.
        await ActivatePluginsAsync(ct).ConfigureAwait(true);

        // 4) PluginIndex im Hintergrund laden.
        _ = LoadPluginIndexAsync(ct);

        // 5) UI-Filter + LastSelection wiederherstellen.
        ApplyFilterAndSort();
        RestoreLastSelection();
        _persistSelection = true;

        // 6) Fresh Discovery im Background — Diff einpflegen.
        _ = RefreshDiscoveryAsync(ct);

        // Plugin-Update-Check im Hintergrund.
        _ = Task.Run(async () =>
        {
            try { await _pluginUpdates.CheckAllAsync(ct); }
            catch (Exception ex) { Log.Debug(ex, "Initial Plugin-Update-Check fehlgeschlagen"); }
        }, ct);
    }

    /// <summary>Fresh Steam-Discovery im Hintergrund. Vergleicht das Ergebnis
    /// mit dem aktuellen <see cref="_allGames"/>-State und synchronisiert:
    /// neue Spiele werden hinzugefügt, verschwundene entfernt. Der Cache wird
    /// nur überschrieben wenn die Discovery tatsächlich etwas Neues zeigt —
    /// bei kaputter Steam-Installation bleibt der Cache erhalten.</summary>
    private async Task RefreshDiscoveryAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await Task.Run(() => _discovery.Discover(), ct).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var freshKeys = new HashSet<string>(fresh.Select(g => g.Key), StringComparer.Ordinal);
                var currentKeys = new HashSet<string>(_allGames.Select(g => g.Key), StringComparer.Ordinal);

                var added = new List<DiscoveredGame>();
                foreach (var g in fresh)
                    if (!currentKeys.Contains(g.Key)) added.Add(g);

                var removed = new List<GameEntry>();
                foreach (var entry in _allGames)
                    if (!freshKeys.Contains(entry.Key)) removed.Add(entry);

                foreach (var g in added) _allGames.Add(new GameEntry(g));
                foreach (var entry in removed) _allGames.Remove(entry);

                if (added.Count > 0 || removed.Count > 0)
                {
                    Log.Info("Discovery-Refresh: +{Added} / -{Removed} Spiel(e)", added.Count, removed.Count);
                    _gamesCache.Save(fresh);
                    ApplyFilterAndSort();
                    RefreshPluginStates();
                    if (added.Count > 0)
                        _ = LoadCoversAsync(added.Select(g => _allGames.First(e => e.Key == g.Key)).ToArray(), ct);
                    StatusText = $"{_allGames.Count} Spiele (aktualisiert: +{added.Count}/-{removed.Count}).";
                }
                else
                {
                    // Cache-Timestamp trotzdem auffrischen — Cache-Files älter als
                    // ein paar Tage würden bei fehlender Steam-Session wieder auf
                    // die alte Liste zeigen.
                    _gamesCache.Save(fresh);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Discovery-Refresh im Hintergrund fehlgeschlagen");
        }
    }

    private async Task LoadPluginIndexAsync(CancellationToken ct)
    {
        try
        {
            var idx = await _pluginIndex.GetAsync(ct).ConfigureAwait(false);
            // Auf UI-Thread setzen, damit Bindings (Sterne + Install-Karte)
            // die Änderungen sicher sehen.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _indexCache = idx;
                RefreshPluginStates();
                Log.Info("Plugin-Index in UI übernommen: {N} Plugin(s), Sterne aktualisiert",
                    idx.Plugins.Count);
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Plugin-Index-Load fehlgeschlagen — nur installierte Sterne");
        }
    }

    private async Task ActivatePluginsAsync(CancellationToken ct)
    {
        try
        {
            var discovered = await Task.Run(_pluginScanner.Scan, ct).ConfigureAwait(true);
            var currentGames = _allGames.Select(g => g.Source).ToList();
            var hostVer = ParseVersion(_hostUpdate.CurrentVersion);
            var decisions = _pluginPlanner.Plan(discovered, currentGames, hostVer);
            await _pluginActivator.ActivateManyAsync(decisions, ct).ConfigureAwait(true);
            RefreshPluginStates();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Aktivierung schlug fehl — App läuft ohne Plugins weiter");
        }
    }

    private static Version ParseVersion(string s)
    {
        int dash = s.IndexOf('-'); if (dash >= 0) s = s[..dash];
        int plus = s.IndexOf('+'); if (plus >= 0) s = s[..plus];
        return Version.TryParse(s, out var v) ? v : new Version(0, 0);
    }

    private void RefreshPluginStates()
    {
        var loaded = _pluginActivator.Loaded;
        var appIdsWithLoadedPlugin = loaded
            .SelectMany(l => l.DetectedGames.Select(dg => dg.Target.SteamAppId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        var appIdsWithAvailablePlugin = _indexCache?.Plugins
            .SelectMany(p => p.SteamAppIds)
            .ToHashSet() ?? new HashSet<int>();

        int installedCount = 0, availableCount = 0;
        foreach (var g in _allGames)
        {
            if (g.Source.SteamAppId is not int appId)
            {
                g.PluginState = PluginState.None;
                continue;
            }
            if (appIdsWithLoadedPlugin.Contains(appId)) { g.PluginState = PluginState.Installed; installedCount++; }
            else if (appIdsWithAvailablePlugin.Contains(appId)) { g.PluginState = PluginState.Available; availableCount++; }
            else g.PluginState = PluginState.None;
        }
        Log.Info("RefreshPluginStates: {Installed} installed, {Available} available (of {Total} games); selected={Sel}",
            installedCount, availableCount, _allGames.Count, SelectedGame?.Key ?? "<none>");

        ApplyFilterAndSort();
        if (SelectedGame is not null) RenderContentForSelected(SelectedGame);
    }

    private async Task LoadCoversAsync(GameEntry[] entries, CancellationToken ct)
    {
        // Simpler serieller Load — bei 30 Spielen ok, für Kohärenz besser als
        // 30 parallele CDN-Requests (Rate-Limit).
        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var path = await _covers.ResolveCoverAsync(
                    entry.Source.SteamAppId, entry.Source.CustomCoverPath, ct).ConfigureAwait(false);
                if (path is null || !File.Exists(path)) continue;
                Bitmap? bmp = null;
                try
                {
                    // Skia auf Linux: Bitmap-Load auf UI-Thread nötig (Renderer-Init).
                    // Wir laden synchron auf UI-Thread; das ist bei 30 Bildern kein Problem.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        using var s = File.OpenRead(path);
                        bmp = new Bitmap(s);
                        entry.Cover = bmp;
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Cover-Bitmap-Load für {Key} fehlgeschlagen ({Path})", entry.Key, path);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Cover-Resolve für {Key} fehlgeschlagen", entry.Key);
            }
        }
    }

    private void ApplyFilterAndSort()
    {
        var q = SearchText?.Trim() ?? string.Empty;

        IEnumerable<GameEntry> filtered = _allGames;
        if (!string.IsNullOrEmpty(q))
            filtered = filtered.Where(g => g.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (OnlyWithPlugin)
            filtered = filtered.Where(g => g.PluginState != PluginState.None);

        var sorted = filtered
            .OrderByDescending(g => g.PluginState == PluginState.Installed)
            .ThenByDescending(g => g.PluginState == PluginState.Available)
            .ThenBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // WICHTIG: Avalonias ListBox setzt SelectedItem auf null wenn die
        // ItemsSource.Clear() aufgerufen wird — bei TwoWay-Binding rennt das
        // durch bis zu unserem SelectedGame und nullt es. Deshalb vorher
        // sichern und nach dem Refill wiederherstellen.
        var previouslySelected = SelectedGame;

        VisibleGames.Clear();
        foreach (var g in sorted) VisibleGames.Add(g);

        if (previouslySelected is not null && VisibleGames.Contains(previouslySelected))
            SelectedGame = previouslySelected;
        else if (SelectedGame is null)
            SelectedGame = VisibleGames.FirstOrDefault();
    }

    private void RestoreLastSelection()
    {
        var lastKey = _settings.Current.LastSelectedGameId;
        SelectedGame = (lastKey is not null
            ? VisibleGames.FirstOrDefault(g => g.Key == lastKey)
            : null) ?? VisibleGames.FirstOrDefault();
    }

    // Cache-Key des letzten Rendern-Zustands: entry.Key + PluginState + LoadedPluginId +
    // (int)IndexCache-Count. Ändert sich nichts davon, ist der Render redundant.
    private string? _lastRenderKey;

    private void RenderContentForSelected(GameEntry entry)
    {
        var loaded = _pluginActivator.Loaded.FirstOrDefault(l => MatchesGame(l, entry));
        var currentKey = $"{entry.Key}|{entry.PluginState}|{loaded?.Manifest.Id ?? ""}|{_indexCache?.Plugins.Count ?? -1}";
        if (currentKey == _lastRenderKey) return;
        _lastRenderKey = currentKey;

        Log.Info("Render {Key} ({Name}) → Loaded={LoadedId} IndexCache={IdxCount} State={State}",
            entry.Key, entry.DisplayName,
            loaded?.Manifest.Id ?? "<none>",
            _indexCache?.Plugins.Count ?? -1,
            entry.PluginState);

        if (loaded is null)
        {
            // Plugin verfügbar, aber nicht installiert? → Install-Karte statt Placeholder.
            var indexEntry = FindIndexEntryFor(entry);
            if (indexEntry is not null)
            {
                InstallCard = new InstallCardViewModel(
                    indexEntry, entry.DisplayName,
                    _pluginInstaller, _pluginActivator, _pluginPlanner,
                    ParseVersion(_hostUpdate.CurrentVersion),
                    // Snapshot der aktuellen Games — der Planner braucht sie um
                    // MatchedGames für das Plugin auszurechnen. Ohne diesen
                    // Snapshot wird das Plugin ohne Spiel-Kontext initialisiert
                    // und die Install-Karte bleibt sichtbar statt der Plugin-Tabs.
                    gamesProvider: () => _allGames.Select(g => g.Source).ToList(),
                    onInstalledLive: async () =>
                    {
                        RefreshPluginStates();
                        RenderContentForSelected(entry);
                        await Task.CompletedTask;
                    });
                ShowInstallCard = true;
                ShowPluginTabs = false;
                ShowContentPlaceholder = false;
                Log.Info("→ Install-Karte gezeigt für Plugin {PluginId}", indexEntry.Id);
                return;
            }

            InstallCard = null;
            ShowInstallCard = false;
            PluginTabs = null;
            ShowPluginTabs = false;
            ShowContentPlaceholder = true;
            ContentPlaceholderText =
                $"Für „{entry.DisplayName}“ ist kein Plugin verfügbar.";
            return;
        }

        // Plugin ist geladen → Install-Karte weg, Tabs zeigen.
        InstallCard = null;
        ShowInstallCard = false;

        var target = loaded.Manifest.Targets.FirstOrDefault(t => t.SteamAppId == entry.Source.SteamAppId);
        var detected = loaded.DetectedGames.FirstOrDefault(dg => dg.Target.SteamAppId == entry.Source.SteamAppId);
        if (target is null || detected is null)
        {
            ShowPluginTabs = false;
            ShowContentPlaceholder = true;
            ContentPlaceholderText = "Plugin liefert für dieses Spiel keine Ansichten.";
            return;
        }

        var tabs = new ObservableCollection<TabItem>();
        foreach (var contribution in loaded.Plugin.GetTabContributions(detected)
                     .Where(c => c.IsVisible(detected))
                     .OrderBy(c => c.Order))
        {
            try
            {
                var view = contribution.CreateView(detected, loaded.Host);
                tabs.Add(new TabItem
                {
                    Header = string.IsNullOrEmpty(contribution.Icon)
                        ? contribution.Label
                        : $"{contribution.Icon}  {contribution.Label}",
                    Content = view,
                });
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Tab-Contribution {Id} vom Plugin {Plugin} warf beim CreateView",
                    contribution.Id, loaded.Manifest.Id);
            }
        }
        PluginTabs = tabs;
        ShowPluginTabs = tabs.Count > 0;
        ShowContentPlaceholder = tabs.Count == 0;
        if (tabs.Count == 0)
            ContentPlaceholderText = "Plugin geladen, aber ohne sichtbare Tabs.";
    }

    private static bool MatchesGame(LoadedPlugin loaded, GameEntry entry)
    {
        if (entry.Source.SteamAppId is not int appId) return false;
        return loaded.DetectedGames.Any(dg => dg.Target.SteamAppId == appId);
    }

    private PluginIndexEntry? FindIndexEntryFor(GameEntry entry)
    {
        if (_indexCache is null) return null;
        if (entry.Source.SteamAppId is not int appId) return null;
        return _indexCache.Plugins.FirstOrDefault(p => p.SteamAppIds.Contains(appId));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _services.GetRequiredService<SettingsWindowViewModel>();
        var window = new SettingsWindow { DataContext = vm };
        var owner = MainWindow();
        if (owner is not null) window.ShowDialog(owner); else window.Show();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var window = new AboutWindow(_hostUpdate);
        var owner = MainWindow();
        if (owner is not null) window.ShowDialog(owner); else window.Show();
    }

    [RelayCommand]
    private void OpenPluginUpdates()
    {
        var vm = new PluginUpdatesViewModel(_pluginUpdates);
        var window = new PluginUpdatesWindow { DataContext = vm };
        var owner = MainWindow();
        if (owner is not null) window.ShowDialog(owner); else window.Show();
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        var vm = new AddGameDialogViewModel(_manual);
        var dialog = new AddGameDialog { DataContext = vm };
        var owner = MainWindow();
        if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
        if (vm.Result is not null)
        {
            var entry = new GameEntry(new DiscoveredGame(
                Key: $"manual:{vm.Result.Id}",
                DisplayName: vm.Result.DisplayName,
                InstallDir: vm.Result.InstallDir,
                SteamAppId: vm.Result.SteamAppId,
                ManualId: vm.Result.Id,
                CustomCoverPath: vm.Result.CoverPath,
                Source: DiscoveredGameSource.Manual));
            _allGames.Add(entry);
            _ = LoadCoversAsync(new[] { entry }, default);
            ApplyFilterAndSort();
            SelectedGame = entry;
        }
    }

    private static Window? MainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
