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
using KroModIx.Plugin.Contracts;
using KroModIx.Services;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using KroModIx.Services.Steam;
using KroModIx.Views;
using NLog;

namespace KroModIx.ViewModels;

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
    private readonly GameLauncherService _launcher;
    private readonly ManualGamesService _manual;
    private readonly PluginRegistryScanner _pluginScanner;
    private readonly PluginActivationPlanner _pluginPlanner;
    private readonly PluginActivator _pluginActivator;
    private readonly PluginIndexService _pluginIndex;
    private readonly PluginInstaller _pluginInstaller;
    private readonly PluginUpdateService _pluginUpdates;
    private readonly PluginUninstaller _pluginUninstaller;
    private readonly GameUpdateBadgeService _updateBadges;
    private readonly NotificationSinkImpl _notifications;
    private readonly AppSettingsService _settings;
    private readonly HostUpdateService _hostUpdate;
    private readonly StatusProgressCoordinator _statusProgress;
    private readonly Services.Ai.AiSettingsService _aiSettings;

    private int _nextToastId;

    private PluginIndex? _indexCache;

    private readonly List<GameEntry> _allGames = new();

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
        _discovery = services.GetRequiredService<GameDiscoveryService>();
        _gamesCache = services.GetRequiredService<GamesCacheService>();
        _covers = services.GetRequiredService<GameCoverService>();
        _launcher = services.GetRequiredService<GameLauncherService>();
        _manual = services.GetRequiredService<ManualGamesService>();
        _pluginScanner = services.GetRequiredService<PluginRegistryScanner>();
        _pluginPlanner = services.GetRequiredService<PluginActivationPlanner>();
        _pluginActivator = services.GetRequiredService<PluginActivator>();
        _pluginIndex = services.GetRequiredService<PluginIndexService>();
        _pluginInstaller = services.GetRequiredService<PluginInstaller>();
        _pluginUpdates = services.GetRequiredService<PluginUpdateService>();
        _pluginUninstaller = services.GetRequiredService<PluginUninstaller>();
        _updateBadges = services.GetRequiredService<GameUpdateBadgeService>();
        _notifications = services.GetRequiredService<NotificationSinkImpl>();
        _settings = services.GetRequiredService<AppSettingsService>();
        _hostUpdate = services.GetRequiredService<HostUpdateService>();
        _statusProgress = services.GetRequiredService<StatusProgressCoordinator>();
        _aiSettings = services.GetRequiredService<Services.Ai.AiSettingsService>();

        _aiSettings.SettingsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAiChip);
        RefreshAiChip();

        // Plugin-Notifications direkt als Toast anzeigen (bislang gingen sie
        // nur in den Log). Marshallen auf UI-Thread, weil Plugins vom Worker-
        // Thread notifizieren dürfen.
        _notifications.Notified += (_, e) => Dispatcher.UIThread.Post(() =>
            EnqueueToast(e.Message, e.Level));

        _pluginActivator.LoadedChanged += (_, _) => Dispatcher.UIThread.Post(RefreshPluginStates);

        _updateBadges.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshUpdateBadges);
        _pluginUpdates.UpdatesChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            AvailableUpdateCount = _pluginUpdates.AvailableUpdates.Count;
        });
        _statusProgress.Changed += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            ProgressIsActive = e.IsActive;
            ProgressTitle = e.Title ?? "";
            ProgressMessage = e.Message ?? "";
            ProgressFraction = e.Fraction;
            ProgressIndeterminate = e.Indeterminate;
        });

        // Persistierten Sidebar-Filter beim Start übernehmen.
        _showAllGames = _settings.Current.SidebarShowAllGames;
    }

    public ObservableCollection<GameEntry> VisibleGames { get; } = new();

    /// <summary>Toast-Overlay unten rechts im MainWindow. Neue Einträge landen
    /// über <see cref="EnqueueToast"/>; nach 6 Sekunden werden sie via Timer
    /// wieder entfernt (kein Fade-Out — für die UX reicht ein instant remove).</summary>
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    [NotifyPropertyChangedFor(nameof(CanLaunchSelected))]
    private GameEntry? _selectedGame;

    public bool HasSelectedGame => SelectedGame is not null;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Sidebar-Filter: wenn true, werden auch Non-Plugin-Games
    /// angezeigt (ausgegraut). Persistiert in <see cref="AppSettings.SidebarShowAllGames"/>.
    /// Default: false (nur Plugin-Games).</summary>
    [ObservableProperty]
    private bool _showAllGames;

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

    // Progress-Anzeige — wird vom StatusProgressCoordinator gefüttert. Plugin-
    // Aktionen wie Downloads rufen IHostServices.BeginProgress → das feuert
    // Changed-Events, die hier landen und die Statusbar aktualisieren.
    [ObservableProperty] private bool _progressIsActive;
    [ObservableProperty] private string _progressTitle = "";
    [ObservableProperty] private string _progressMessage = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgressFraction))]
    private double? _progressFraction;
    [ObservableProperty] private bool _progressIndeterminate;

    public bool HasProgressFraction => ProgressFraction is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdates))]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private int _availableUpdateCount;

    public bool HasAvailableUpdates => AvailableUpdateCount > 0;
    public string UpdateBadgeText => AvailableUpdateCount > 0 ? $"↑ {AvailableUpdateCount}" : "";

    /// <summary>Kurz-Bezeichner des aktiven KI-Modells fürs Header-Chip
    /// (z.B. „Ollama · llama3.1:8b"). Leer wenn kein Provider konfiguriert.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAiChip))]
    private string _aiChipLabel = "";

    [ObservableProperty] private string _aiChipTooltip = "";

    public bool HasAiChip => !string.IsNullOrEmpty(AiChipLabel);

    private void RefreshAiChip()
    {
        var s = _aiSettings.Current;
        if (s.Provider == Services.Ai.AiProviderType.None)
        {
            AiChipLabel = "";
            AiChipTooltip = "";
            return;
        }
        var cfg = s.Active;
        var providerName = s.Provider switch
        {
            Services.Ai.AiProviderType.Ollama => "Ollama",
            Services.Ai.AiProviderType.Anthropic => "Anthropic",
            Services.Ai.AiProviderType.OpenAi => "OpenAI",
            Services.Ai.AiProviderType.Gemini => "Gemini",
            Services.Ai.AiProviderType.Mistral => "Mistral",
            Services.Ai.AiProviderType.OpenAiCompatible => "API",
            _ => s.Provider.ToString(),
        };
        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "?" : cfg.Model;
        AiChipLabel = $"🤖 {providerName} · {model}";
        AiChipTooltip = $"KI-Provider: {providerName}\nModell: {model}\nEndpoint: {cfg.Endpoint}\n\nKlick öffnet Einstellungen.";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();
    partial void OnShowAllGamesChanged(bool value)
    {
        _settings.Update(s => s.SidebarShowAllGames = value);
        ApplyFilterAndSort();
        RefreshDimmingFlags();
    }

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

        // 5) UI-Filter + Dimming + LastSelection wiederherstellen.
        RefreshDimmingFlags();
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

        // Mod-Update-Badges-Loop starten (Plugins mit IUpdateNotifier).
        // Refresh alle 30min, mit initialem 10s-Delay damit der Discovery-
        // Rush erst durchläuft.
        _updateBadges.Start();
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

                    // 4.2: Discovery-Diff als Toasts. Kompakt formatiert
                    // (bis zu 3 Namen, sonst „+N weitere") damit die Karten
                    // nicht überquellen wenn Steam viele Spiele gleichzeitig
                    // meldet (z.B. nach einer neu gemounteten Library-Platte).
                    if (added.Count > 0)
                        EnqueueToast($"🎮 +{added.Count} Spiel(e): {FormatGameList(added.Select(g => g.DisplayName))}",
                            NotificationLevel.Info);
                    if (removed.Count > 0)
                        EnqueueToast($"🗑 -{removed.Count} Spiel(e): {FormatGameList(removed.Select(g => g.DisplayName))}",
                            NotificationLevel.Warning);

                    // 4.3: Auto-Cleanup — wenn Setting aktiv UND für ein
                    // geladenes Plugin kein Zielspiel mehr da ist, Plugin-
                    // Ordner löschen. Zur Runtime bleibt das Plugin geladen
                    // (kein AssemblyLoadContext-Unload — Checkmk-Erfahrung),
                    // beim nächsten Start ist es weg.
                    if (_settings.Current.PluginAutoCleanupOnGameUninstall)
                        _ = RunAutoCleanupAsync(fresh, removed, ct);
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

            // Plugins mit VirtualGame (z. B. RenPyAssist ohne echten Steam-
            // Bezug): Manual-Anker anlegen falls noch keiner mit der
            // SteamAppId existiert, danach die Sidebar-Games neu einlesen
            // damit der neue Anker im ersten Plan mitgezählt wird.
            bool anyEnsured = false;
            foreach (var disc in discovered)
            {
                var vg = disc.Manifest.VirtualGame;
                if (vg is null || vg.SteamAppId == 0
                    || string.IsNullOrWhiteSpace(vg.DisplayName)) continue;
                if (_manual.EnsureVirtualAnchor(vg.DisplayName, vg.SteamAppId))
                    anyEnsured = true;
            }
            if (anyEnsured)
            {
                var refreshed = await Task.Run(() => _discovery.Discover(), ct).ConfigureAwait(true);
                _allGames.Clear();
                foreach (var g in refreshed) _allGames.Add(new GameEntry(g));
                _gamesCache.Save(refreshed);
                Log.Info("Virtual-Anchor(s) angelegt — Sidebar neu geladen: {N} Spiele", _allGames.Count);
            }

            var currentGames = _allGames.Select(g => g.Source).ToList();
            var hostVer = ParseVersion(_hostUpdate.CurrentVersion);
            var decisions = _pluginPlanner.Plan(discovered, currentGames, hostVer);
            await _pluginActivator.ActivateManyAsync(decisions, ct).ConfigureAwait(true);
            RefreshPluginStates();

            if (anyEnsured)
            {
                // Erst nach Aktivierung Filter/Sort neu anwenden — neue Kachel
                // taucht sonst nicht mit korrektem PluginState in der Liste auf.
                ApplyFilterAndSort();
            }
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
        RefreshDimmingFlags();
        if (SelectedGame is not null) RenderContentForSelected(SelectedGame);
    }

    /// <summary>Fügt einen Toast ins Overlay ein und plant sein Auto-Remove
    /// nach der angegebenen Dauer. Muss auf dem UI-Thread aufgerufen werden
    /// (die Kollektion ist an Avalonia-Bindings gekoppelt).</summary>
    public void EnqueueToast(string message, NotificationLevel level = NotificationLevel.Info,
        TimeSpan? duration = null)
    {
        var d = duration ?? TimeSpan.FromSeconds(6);
        var id = System.Threading.Interlocked.Increment(ref _nextToastId);
        var toast = new ToastItem(id, message, level);
        Toasts.Add(toast);
        _ = Task.Run(async () =>
        {
            await Task.Delay(d);
            await Dispatcher.UIThread.InvokeAsync(() => Toasts.Remove(toast));
        });
    }

    [RelayCommand]
    private void DismissToast(ToastItem? toast)
    {
        if (toast is not null) Toasts.Remove(toast);
    }

    private static string FormatGameList(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count <= 3) return string.Join(", ", list);
        return string.Join(", ", list.Take(3)) + $", +{list.Count - 3} weitere";
    }

    /// <summary>Prüft für jedes geladene Plugin, ob unter den <paramref name="fresh"/>-
    /// Games noch ein Zielspiel existiert. Wenn nicht → <see cref="PluginUninstaller.Uninstall"/>
    /// löscht den Plugin-Ordner unter <c>~/.config/KroModIx/plugins/</c>. Toast
    /// informiert den User über die Aktion. Läuft im Hintergrund, keine
    /// Blockierung des Discovery-Refreshes.</summary>
    private async Task RunAutoCleanupAsync(IReadOnlyList<DiscoveredGame> fresh,
        List<GameEntry> removed, CancellationToken ct)
    {
        if (removed.Count == 0) return;
        var freshAppIds = new HashSet<int>(fresh.Select(g => g.SteamAppId).OfType<int>());
        var candidates = _pluginActivator.Loaded
            .Where(l => l.Manifest.Targets.All(t => t.SteamAppId is not int id || !freshAppIds.Contains(id)))
            .ToList();

        foreach (var loaded in candidates)
        {
            try
            {
                // Auto-Cleanup: nur Plugin-Assembly weg, User-Data + Cache
                // behalten — die will man beim eventuellen Re-Install nicht
                // verlieren (Nexus-Key, Katalog-Snapshot etc.).
                _pluginUninstaller.Uninstall(loaded.Manifest.Id, deleteData: false, deleteCache: false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    EnqueueToast(
                        $"🧹 Auto-Cleanup: Plugin '{loaded.Manifest.DisplayName}' entfernt (keine Zielspiele mehr installiert). Beim nächsten Start weg.",
                        NotificationLevel.Warning,
                        TimeSpan.FromSeconds(10)));
                Log.Info("Auto-Cleanup: Plugin {Id} deinstalliert", loaded.Manifest.Id);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Auto-Cleanup für Plugin {Id} fehlgeschlagen", loaded.Manifest.Id);
            }
            if (ct.IsCancellationRequested) break;
        }
    }

    /// <summary>Setzt <see cref="GameEntry.PendingUpdateCount"/> + Tooltip aus
    /// <see cref="GameUpdateBadgeService.Pending"/>. Läuft auf dem UI-Thread —
    /// die Bindings triggern das Neuzeichnen des grünen ↑-Badges pro Kachel.</summary>
    private void RefreshUpdateBadges()
    {
        var pending = _updateBadges.Pending;
        foreach (var g in _allGames)
        {
            if (g.Source.SteamAppId is int appId && pending.TryGetValue(appId, out var info))
            {
                g.PendingUpdateCount = info.PendingCount;
                g.UpdateBadgeTooltip = info.Summary ?? $"{info.PendingCount} Update(s) verfügbar";
            }
            else
            {
                g.PendingUpdateCount = 0;
                g.UpdateBadgeTooltip = null;
            }
        }
    }

    /// <summary>Setzt <see cref="GameEntry.IsDimmed"/> für alle Spiele.
    /// Dimming greift nur wenn <see cref="ShowAllGames"/> aktiv ist UND das
    /// Spiel kein Plugin hat — bei „nur mit Plugin" (Default-Filter) sind
    /// alle sichtbaren Spiele voll deckend.</summary>
    private void RefreshDimmingFlags()
    {
        foreach (var g in _allGames)
            g.IsDimmed = ShowAllGames && g.PluginState == PluginState.None;
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
                // User-Override aus dem Sidebar-Kontextmenü hat Vorrang vor
                // dem Manual-Game CustomCoverPath aus dem AddGame-Dialog.
                var userOverride = _settings.Current.CustomGameCovers is { } dict
                                    && dict.TryGetValue(entry.Key, out var op) ? op : null;
                var customPath = userOverride ?? entry.Source.CustomCoverPath;
                var path = await _covers.ResolveCoverAsync(
                    entry.Source.SteamAppId, customPath, ct).ConfigureAwait(false);
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

        // Vom User versteckte Spiele (Kontextmenü „Aus KroModIx entfernen" auf
        // einem Steam-Spiel) niemals in der Sidebar zeigen — auch nicht wenn
        // ShowAllGames aktiv ist.
        var hidden = new HashSet<string>(_settings.Current.HiddenGameKeys, StringComparer.Ordinal);

        IEnumerable<GameEntry> filtered = _allGames.Where(g => !hidden.Contains(g.Key));
        if (!string.IsNullOrEmpty(q))
            filtered = filtered.Where(g => g.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        // Default: nur mit Plugin. ShowAllGames aktiv → alles (Non-Plugin-Games
        // werden im XAML ausgegraut via IsDimmed).
        if (!ShowAllGames)
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
                    // Name = "PluginTab_<id>" macht den Tab per REST-API
                    // (/events/click mit elementId="PluginTab_catalog") ansprechbar.
                    // Tag hält die reine tabId für den kommenden /select-tab-Endpoint.
                    Name = $"PluginTab_{contribution.Id}",
                    Tag = contribution.Id,
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

    /// <summary>Startet das aktuell ausgewählte Spiel. Wird sowohl vom
    /// „▶ Starten"-Button im Content-Header als auch vom Sidebar-
    /// Doppelklick aufgerufen. Ohne Selection ein No-Op.</summary>
    [RelayCommand]
    private void LaunchSelectedGame()
    {
        if (SelectedGame is null) return;
        var result = _launcher.Launch(SelectedGame.Source);
        StatusText = result.Message;
        Log.Info("LaunchSelectedGame → {Ok}: {Msg}", result.Success, result.Message);
    }

    public bool CanLaunchSelected =>
        SelectedGame is not null &&
        (SelectedGame.Source.SteamAppId is not null || !string.IsNullOrWhiteSpace(SelectedGame.Source.ExecutablePath));

    [RelayCommand]
    private void OpenPluginUpdates()
    {
        // Ehemals „Plugin-Updates" — jetzt kompletter Plugin-Manager mit
        // Updates-Sektion + installierte-Plugins-Sektion + Uninstall.
        var vm = _services.GetRequiredService<PluginUpdatesViewModel>();
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

    /// <summary>Öffnet den „🎮 Ordner mit Spielen scannen"-Wizard: User wählt
    /// einen Root, der Host scannt nach Engine-Signaturen und legt pro
    /// gefundenem Container eine eigene Sidebar-Kachel an. Die Kacheln matchen
    /// gegen <c>PluginManifest.Targets[].Engine</c>.</summary>
    [RelayCommand]
    private async Task AddFolderCollectionAsync()
    {
        var detector = _services.GetRequiredService<FolderEngineDetector>();
        var vm = new AddFolderCollectionDialogViewModel(_manual, detector);
        var dialog = new AddFolderCollectionDialog { DataContext = vm };
        var owner = MainWindow();
        if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
        if (vm.Results.Count == 0) return;

        var newEntries = new List<GameEntry>();
        foreach (var r in vm.Results)
        {
            var entry = new GameEntry(new DiscoveredGame(
                Key: $"manual:{r.Id}",
                DisplayName: r.DisplayName,
                InstallDir: r.InstallDir,
                SteamAppId: r.SteamAppId,
                ManualId: r.Id,
                CustomCoverPath: r.CoverPath,
                Source: DiscoveredGameSource.Manual,
                ExecutablePath: r.ExecutablePath,
                Engine: r.Engine));
            _allGames.Add(entry);
            newEntries.Add(entry);
        }
        _ = LoadCoversAsync(newEntries.ToArray(), default);

        // Plugin-Aktivierung neu triggern — die neuen Engine-Kacheln matchen
        // gegen Plugins mit passendem GameTarget.Engine.
        await ActivatePluginsAsync(default);
        ApplyFilterAndSort();
        if (newEntries.Count > 0) SelectedGame = newEntries[0];
        EnqueueToast($"🎮 {newEntries.Count} Spiel(e) importiert", NotificationLevel.Success);
    }

    /// <summary>Sidebar-Kontextmenü „🖼 Kachelbild ändern": öffnet File-Picker,
    /// kopiert das ausgewählte Bild in <see cref="AppPaths.UserCoverDir"/>
    /// (persistent an unser Cache-Verzeichnis gebunden, damit der User seinen
    /// Ordner umbenennen kann), speichert den Pfad in
    /// <see cref="AppSettings.CustomGameCovers"/> und lädt die Kachel neu.</summary>
    [RelayCommand]
    private async Task ChangeCoverAsync(GameEntry? entry)
    {
        if (entry is null) return;
        var dialog = _services.GetRequiredService<IDialogService>();
        var picked = await dialog.PickFileAsync(
            $"Neues Kachelbild für '{entry.DisplayName}'",
            ("Bilder", new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }));
        if (string.IsNullOrWhiteSpace(picked) || !File.Exists(picked)) return;

        try
        {
            var ext = Path.GetExtension(picked);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            // Ordner-fähigen Dateinamen aus dem Key ableiten (steam:2300320 → steam_2300320)
            var safeKey = entry.Key.Replace(':', '_').Replace('/', '_');
            var target = Path.Combine(AppPaths.UserCoverDir, safeKey + ext);
            File.Copy(picked, target, overwrite: true);

            _settings.Update(s =>
            {
                s.CustomGameCovers ??= new Dictionary<string, string>();
                s.CustomGameCovers[entry.Key] = target;
            });

            // Cover neu laden — LoadCoversAsync bevorzugt den CustomGameCovers-Override.
            _ = LoadCoversAsync(new[] { entry }, default);
            StatusText = $"Kachelbild für '{entry.DisplayName}' aktualisiert.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kachelbild-Wechsel für {Key} fehlgeschlagen", entry.Key);
            StatusText = $"Fehler beim Setzen des Kachelbilds: {ex.Message}";
        }
    }

    /// <summary>Sidebar-Kontextmenü „🗑 Aus KroModIx entfernen": bei Manual-
    /// Games löscht es den Eintrag in <see cref="ManualGamesService"/>; bei
    /// Steam-Games (die von der Steam-Discovery immer wieder auftauchen)
    /// merkt es sich den Key in <see cref="AppSettings.HiddenGameKeys"/>
    /// als Blacklist, sodass die Discovery ihn beim nächsten Refresh
    /// ausfiltert. In beiden Fällen sofortige UI-Aktualisierung.</summary>
    [RelayCommand]
    private async Task RemoveGameAsync(GameEntry? entry)
    {
        if (entry is null) return;
        var dialog = _services.GetRequiredService<IDialogService>();
        var confirmed = await dialog.ConfirmAsync(
            title: "Spiel entfernen?",
            message: entry.IsManual
                ? $"'{entry.DisplayName}' aus KroModIx entfernen? Der Manual-Eintrag wird gelöscht (Steam-Ordner bleibt unangetastet)."
                : $"'{entry.DisplayName}' aus der Sidebar ausblenden? Steam-Discovery findet es beim nächsten Refresh wieder — die Blacklist verhindert die Anzeige.");
        if (!confirmed) return;

        if (entry.IsManual && !string.IsNullOrEmpty(entry.Source.ManualId))
        {
            _manual.Remove(entry.Source.ManualId);
        }
        else
        {
            _settings.Update(s =>
            {
                s.HiddenGameKeys ??= new List<string>();
                if (!s.HiddenGameKeys.Contains(entry.Key))
                    s.HiddenGameKeys.Add(entry.Key);
            });
        }
        _allGames.Remove(entry);
        if (ReferenceEquals(SelectedGame, entry)) SelectedGame = null;
        ApplyFilterAndSort();
        StatusText = $"'{entry.DisplayName}' entfernt.";
    }

    private static Window? MainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
