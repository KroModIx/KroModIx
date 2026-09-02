using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Services;
using KroModIx.Services.Plugins;
using NLog;

namespace KroModIx.ViewModels;

/// <summary>VM für das Plugin-Manager-Fenster (verfügbare Updates + installierte
/// Plugins mit Uninstall). Restart-Hint kommt nach Install/Uninstall — die
/// geladenen Assemblies bleiben bis App-Restart im Prozess.</summary>
public sealed partial class PluginUpdatesViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PluginUpdateService _updates;
    private readonly PluginRegistryScanner _scanner;
    private readonly PluginActivator _activator;
    private readonly PluginUninstaller _uninstaller;
    private readonly PluginIndexService _pluginIndex;
    private readonly IDialogService _dialogs;
    private readonly PluginAutoInstallService _autoInstall;

    public PluginUpdatesViewModel(
        PluginUpdateService updates,
        PluginRegistryScanner scanner,
        PluginActivator activator,
        PluginUninstaller uninstaller,
        PluginIndexService pluginIndex,
        IDialogService dialogs,
        PluginAutoInstallService autoInstall)
    {
        _updates = updates;
        _scanner = scanner;
        _activator = activator;
        _uninstaller = uninstaller;
        _pluginIndex = pluginIndex;
        _dialogs = dialogs;
        _autoInstall = autoInstall;
        RefreshUpdates();
        RefreshInstalled();
        // UpdatesChanged feuert i.d.R. vom Background-Thread des Update-
        // Checks; ObservableCollection<T> ist nicht thread-safe. Ohne
        // Dispatch fuehrten Clear/Add-Sequenzen zu Duplikaten in der
        // Installed-Liste (screenshot v1.14.2 → alle Plugins doppelt).
        _updates.UpdatesChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshUpdates);
    }

    public ObservableCollection<UpdateRow> Rows { get; } = new();
    public ObservableCollection<InstalledPluginRow> Installed { get; } = new();

    [ObservableProperty] private bool _restartHinted;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _installedStatus = "";
    /// <summary>Suchfilter — matcht case-insensitive gegen DisplayName und
    /// PluginId in beiden Listen (Updates + Installed).</summary>
    [ObservableProperty] private string _searchText = "";

    // Ungefilterter Cache damit ein Filter-Toggle ohne Netz-Refresh geht.
    private readonly List<UpdateRow> _allUpdates = new();
    private readonly List<InstalledPluginRow> _allInstalled = new();

    partial void OnSearchTextChanged(string value)
    {
        ApplyUpdatesFilter();
        ApplyInstalledFilter();
    }

    /// <summary>Filtert und rendert nur die Updates-Liste. Getrennt von
    /// <see cref="ApplyInstalledFilter"/> damit ein Refresh der Updates die
    /// Installed-Collection nicht mit anfasst — Clear+Add auf einer nicht-UI-
    /// Thread-Sequenz produzierte sonst Row-Duplikate in der Installed-
    /// Liste (v1.14.2 Bug: 6 Plugins wurden als 12 gerendert).</summary>
    private void ApplyUpdatesFilter()
    {
        var q = (SearchText ?? "").Trim();
        Rows.Clear();
        foreach (var r in _allUpdates)
            if (Match(q, r.DisplayName, r.Source.PluginId)) Rows.Add(r);
        StatusMessage = Rows.Count == 0
            ? (_allUpdates.Count == 0 ? "Keine Updates verfügbar." : "Kein Update matcht den Filter.")
            : "";
    }

    private void ApplyInstalledFilter()
    {
        var q = (SearchText ?? "").Trim();
        Installed.Clear();
        foreach (var r in _allInstalled)
            if (Match(q, r.DisplayName, r.PluginId)) Installed.Add(r);
        InstalledStatus = _allInstalled.Count == 0
            ? "Keine Plugins installiert."
            : (Installed.Count == _allInstalled.Count
                ? $"{_allInstalled.Count} Plugin(s) installiert."
                : $"{Installed.Count} von {_allInstalled.Count} Plugin(s) matchen den Filter.");
    }

    private static bool Match(string query, string displayName, string pluginId)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return displayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || pluginId.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshUpdates()
    {
        _allUpdates.Clear();
        // Defensive UI-Side-Dedup per PluginId — falls der Service trotz
        // SemaphoreSlim mal doppelt einfügt (bei race conditions oder Event-
        // Reentrancy), soll die UI trotzdem sauber bleiben.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in _updates.AvailableUpdates)
            if (seen.Add(u.PluginId))
                _allUpdates.Add(new UpdateRow(u));
        ApplyUpdatesFilter();
    }

    private void RefreshInstalled()
    {
        _allInstalled.Clear();
        try
        {
            var scanned = _scanner.Scan();
            var loadedIds = new HashSet<string>(
                _activator.Loaded.Select(l => l.Manifest.Id), StringComparer.OrdinalIgnoreCase);
            // Dedup per PluginId: PluginRegistryScanner liefert ein Manifest
            // pro (BundledPluginsDir, UserPluginsDir)-Ordner — wenn ein
            // Plugin in beiden liegt (Bundled-App-Install + User-Update)
            // taucht es zweimal auf. Bevorzugung: User (spaeter im Enumerate-
            // Order) mit hoeherer SemVer waere korrekt, aber der Uninstall-
            // Ziel-Dir muss stimmen — daher IsUserInstalled=true vorziehen.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in scanned
                .OrderBy(p => IsUnderUserPluginsDir(p.Directory) ? 0 : 1)
                .ThenBy(p => p.Manifest.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!seen.Add(p.Manifest.Id)) continue;
                var isUser = IsUnderUserPluginsDir(p.Directory);
                _allInstalled.Add(new InstalledPluginRow
                {
                    PluginId = p.Manifest.Id,
                    DisplayName = p.Manifest.DisplayName,
                    Version = p.Manifest.Version,
                    Author = p.Manifest.Author ?? "",
                    PluginDir = p.Directory,
                    IsUserInstalled = isUser,
                    IsLoaded = loadedIds.Contains(p.Manifest.Id),
                });
            }
            // Nach dem Ordner-Prio-Sort wieder alphabetisch fuer die Anzeige.
            _allInstalled.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(
                a.DisplayName, b.DisplayName));
            ApplyInstalledFilter();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Installed-Plugin-Scan fehlgeschlagen");
            InstalledStatus = $"Fehler beim Scannen: {ex.Message}";
        }
    }

    private static bool IsUnderUserPluginsDir(string pluginDir)
    {
        try
        {
            var full = Path.GetFullPath(pluginDir);
            var root = Path.GetFullPath(AppPaths.UserPluginsDir);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        StatusMessage = "Prüfe …";
        // v1.19.1: PluginIndex zwangs-refreshen — sonst sieht der User neue
        // Plugins im Katalog erst nach Cache-TTL (frueher 24h, jetzt 6h).
        // Der Klick auf „Jetzt pruefen" ist genau der Moment wo der User
        // erwartet dass die Sidebar/Install-Card frische Kandidaten zeigt.
        var freshIndex = await _pluginIndex.RefreshAsync();
        int n = await _updates.CheckAllAsync();
        StatusMessage = n == 0
            ? $"Keine Updates verfügbar. Katalog: {freshIndex.Plugins.Count} Plugin(s)."
            : $"{n} Update(s) verfügbar. Katalog: {freshIndex.Plugins.Count} Plugin(s).";
    }

    [RelayCommand]
    private async Task InstallAsync(UpdateRow? row)
    {
        if (row is null) return;
        row.Status = "Lade …";
        try
        {
            bool ok = await _updates.InstallUpdateAsync(row.Source);
            row.Status = ok ? "Installiert (Neustart nötig)" : "Fehlgeschlagen";
            if (ok) RestartHinted = true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Install-Klick für {Id} warf", row.Source.PluginId);
            row.Status = $"Fehler: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(InstalledPluginRow? row)
    {
        if (row is null || !row.IsUserInstalled) return;

        bool ok = await _dialogs.ConfirmAsync(
            "Plugin deinstallieren",
            $"„{row.DisplayName}\" (v{row.Version}) wirklich deinstallieren?\n\n" +
            "Der Plugin-Ordner sowie Cache und Config des Plugins werden gelöscht. " +
            "Die geladene Assembly bleibt bis zum App-Neustart im Prozess — " +
            "Plugin-Tabs verschwinden erst nach Neustart.",
            okLabel: "Deinstallieren",
            cancelLabel: "Abbrechen");
        if (!ok) return;

        try
        {
            var result = _uninstaller.Uninstall(row.PluginId, deleteData: true, deleteCache: true);
            // Ohne Opt-out wuerde der Auto-Install (v1.28.1) das Plugin beim
            // naechsten Start wieder holen — „deinstallieren" waere wirkungslos.
            if (result.PluginDirRemoved) _autoInstall.OptOut(row.PluginId);
            row.Status = result.PluginDirRemoved
                ? "Deinstalliert (Neustart nötig)"
                : "Plugin-Ordner konnte nicht gelöscht werden (siehe Log)";
            if (result.PluginDirRemoved) RestartHinted = true;
            RefreshInstalled();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Uninstall-Klick für {Id} warf", row.PluginId);
            row.Status = $"Fehler: {ex.Message}";
        }
    }
}

public sealed partial class UpdateRow : ObservableObject
{
    public UpdateRow(PluginUpdateInfo source) => Source = source;
    public PluginUpdateInfo Source { get; }
    public string DisplayName => Source.PluginDisplayName;
    public string VersionLabel => $"{Source.InstalledVersion}  →  {Source.LatestVersion}";
    public string AssetName => Source.AssetName ?? "";
    [ObservableProperty] private string _status = "";
}

public sealed partial class InstalledPluginRow : ObservableObject
{
    public string PluginId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string PluginDir { get; init; } = "";
    /// <summary>Bundled-Plugins (neben der Exe) sind read-only — kein Uninstall.</summary>
    public bool IsUserInstalled { get; init; }
    public bool IsLoaded { get; init; }
    public string VersionLabel => $"v{Version}" + (Author.Length > 0 ? $" · {Author}" : "");
    public string LoadedBadge => IsLoaded ? "geladen" : "";
    public string SourceBadge => IsUserInstalled ? "" : "bundled";

    [ObservableProperty] private string _status = "";
}
