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
    private readonly GameCoverService _covers;
    private readonly ManualGamesService _manual;
    private readonly PluginRegistryScanner _pluginScanner;
    private readonly PluginActivationPlanner _pluginPlanner;
    private readonly PluginActivator _pluginActivator;
    private readonly AppSettingsService _settings;
    private readonly HostUpdateService _hostUpdate;

    private readonly List<GameEntry> _allGames = new();

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
        _discovery = services.GetRequiredService<GameDiscoveryService>();
        _covers = services.GetRequiredService<GameCoverService>();
        _manual = services.GetRequiredService<ManualGamesService>();
        _pluginScanner = services.GetRequiredService<PluginRegistryScanner>();
        _pluginPlanner = services.GetRequiredService<PluginActivationPlanner>();
        _pluginActivator = services.GetRequiredService<PluginActivator>();
        _settings = services.GetRequiredService<AppSettingsService>();
        _hostUpdate = services.GetRequiredService<HostUpdateService>();

        _pluginActivator.LoadedChanged += (_, _) => Dispatcher.UIThread.Post(RefreshPluginStates);
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
    private string _contentPlaceholderText = "";

    [ObservableProperty]
    private bool _showPluginTabs;

    [ObservableProperty]
    private bool _showContentPlaceholder = true;

    [ObservableProperty]
    private string _statusText = "Starte …";

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();
    partial void OnOnlyWithPluginChanged(bool value) => ApplyFilterAndSort();

    partial void OnSelectedGameChanged(GameEntry? value)
    {
        if (value is null) { PluginTabs = null; ShowPluginTabs = false; return; }
        RenderContentForSelected(value);
        _settings.Update(s => s.LastSelectedGameId = value.Key);
    }

    /// <summary>Kompletter Init-Ablauf beim App-Start: Discovery → Cover-Load
    /// → Plugin-Scan+Activation → UI aktualisieren.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        StatusText = "Discovery …";
        var games = await Task.Run(() => _discovery.Discover(), ct).ConfigureAwait(true);

        _allGames.Clear();
        foreach (var g in games) _allGames.Add(new GameEntry(g));
        Log.Info("Sidebar: {Count} Spiele geladen", _allGames.Count);

        // Cover parallel im Background laden, aber nur limitiert. Cover werden
        // im UI angezeigt sobald sie verfügbar sind (Bitmap-Property ist Observable).
        _ = LoadCoversAsync(_allGames.ToArray(), ct);

        // Plugin-Discovery + Activation
        await ActivatePluginsAsync(ct).ConfigureAwait(true);

        ApplyFilterAndSort();
        RestoreLastSelection();
        StatusText = $"{_allGames.Count} Spiele erkannt.";
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

        foreach (var g in _allGames)
        {
            g.PluginState = g.Source.SteamAppId is int appId && appIdsWithLoadedPlugin.Contains(appId)
                ? PluginState.Installed
                : PluginState.None;
        }

        // Neu-sortieren, damit Plugin-Spiele nach vorne rutschen
        ApplyFilterAndSort();

        // Content für aktuell selektiertes Spiel ggf. neu rendern (Plugin ist neu da)
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

        VisibleGames.Clear();
        foreach (var g in sorted) VisibleGames.Add(g);

        // Selektion beibehalten wenn noch sichtbar
        if (SelectedGame is not null && !VisibleGames.Contains(SelectedGame))
            SelectedGame = VisibleGames.FirstOrDefault();
    }

    private void RestoreLastSelection()
    {
        var lastKey = _settings.Current.LastSelectedGameId;
        SelectedGame = (lastKey is not null
            ? VisibleGames.FirstOrDefault(g => g.Key == lastKey)
            : null) ?? VisibleGames.FirstOrDefault();
    }

    private void RenderContentForSelected(GameEntry entry)
    {
        var loaded = _pluginActivator.Loaded.FirstOrDefault(l => MatchesGame(l, entry));
        if (loaded is null)
        {
            PluginTabs = null;
            ShowPluginTabs = false;
            ShowContentPlaceholder = true;
            ContentPlaceholderText =
                $"Für „{entry.DisplayName}“ ist noch kein Plugin verfügbar.\n" +
                "In einer späteren Version zeigt sich hier die Plugin-Install-Karte.";
            return;
        }

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
