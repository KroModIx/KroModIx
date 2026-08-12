using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly IDialogService _dialogs;

    public PluginUpdatesViewModel(
        PluginUpdateService updates,
        PluginRegistryScanner scanner,
        PluginActivator activator,
        PluginUninstaller uninstaller,
        IDialogService dialogs)
    {
        _updates = updates;
        _scanner = scanner;
        _activator = activator;
        _uninstaller = uninstaller;
        _dialogs = dialogs;
        RefreshUpdates();
        RefreshInstalled();
        _updates.UpdatesChanged += (_, _) => RefreshUpdates();
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
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var q = (SearchText ?? "").Trim();
        Rows.Clear();
        foreach (var r in _allUpdates)
            if (Match(q, r.DisplayName, r.Source.PluginId)) Rows.Add(r);
        Installed.Clear();
        foreach (var r in _allInstalled)
            if (Match(q, r.DisplayName, r.PluginId)) Installed.Add(r);
        StatusMessage = Rows.Count == 0
            ? (_allUpdates.Count == 0 ? "Keine Updates verfügbar." : "Kein Update matcht den Filter.")
            : "";
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
        ApplyFilters();
    }

    private void RefreshInstalled()
    {
        _allInstalled.Clear();
        try
        {
            var scanned = _scanner.Scan();
            var loadedIds = new HashSet<string>(
                _activator.Loaded.Select(l => l.Manifest.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var p in scanned.OrderBy(p => p.Manifest.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
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
            ApplyFilters();
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
        int n = await _updates.CheckAllAsync();
        StatusMessage = n == 0 ? "Keine Updates verfügbar." : $"{n} Update(s) verfügbar.";
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
