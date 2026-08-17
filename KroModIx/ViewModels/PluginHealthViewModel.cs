using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Services;
using KroModIx.Services.Plugins;

namespace KroModIx.ViewModels;

/// <summary>v1.22.0: Plugin-Health-Dashboard fuer den Settings-Tab.
/// Aggregiert pro geladenem Plugin: Manifest-Version, Bundle-Groesse
/// (Summe aller Dateien im Plugin-Ordner), letzter Update-Check aus dem
/// <see cref="PluginUpdateService"/>-Cache, Zielspiele-Count. Kein
/// Fehler-Feed — der wuerde einen zentralen ErrorTracker brauchen und
/// den Scope sprengen. Rein Read-only, Refresh via Button.</summary>
public sealed partial class PluginHealthViewModel : ObservableObject
{
    private readonly PluginActivator _activator;
    private readonly PluginRegistryScanner _scanner;
    private readonly PluginUpdateService _updates;

    public ObservableCollection<PluginHealthRow> Rows { get; } = new();

    [ObservableProperty] private string _summary = "";

    public PluginHealthViewModel(
        PluginActivator activator,
        PluginRegistryScanner scanner,
        PluginUpdateService updates)
    {
        _activator = activator;
        _scanner = scanner;
        _updates = updates;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        Rows.Clear();
        var discovered = SafeScan().ToDictionary(d => d.Manifest.Id, d => d,
            StringComparer.OrdinalIgnoreCase);

        long totalBytes = 0;
        foreach (var lp in _activator.Loaded.OrderBy(l => l.Manifest.DisplayName))
        {
            discovered.TryGetValue(lp.Manifest.Id, out var disc);
            var dir = disc?.Directory;
            var (bytes, files) = DirSize(dir);
            totalBytes += bytes;

            var isUser = dir is not null
                && dir.StartsWith(Path.GetFullPath(AppPaths.UserPluginsDir),
                    StringComparison.OrdinalIgnoreCase);

            var cached = _updates.TryGetCachedRelease(lp.Manifest.Id);
            var check = cached is null
                ? "noch nie geprueft"
                : $"zuletzt geprueft {FormatRelative(cached.CheckedAtUtc)} · letzter Tag: {cached.LatestTag}";

            Rows.Add(new PluginHealthRow(
                Id: lp.Manifest.Id,
                DisplayName: lp.Manifest.DisplayName,
                Version: lp.Manifest.Version,
                MinHostVersion: lp.Manifest.MinHostVersion ?? "-",
                TargetGamesCount: lp.DetectedGames.Count,
                BundleSizeLabel: FormatBytes(bytes),
                BundleFileCount: files,
                InstallSource: isUser ? "user" : (dir is null ? "?" : "bundled"),
                UpdateCheckLabel: check));
        }

        Summary = Rows.Count == 0
            ? "Keine Plugins geladen."
            : $"{Rows.Count} Plugin(s) geladen · Bundle gesamt: {FormatBytes(totalBytes)}";
    }

    private IEnumerable<DiscoveredPlugin> SafeScan()
    {
        try { return _scanner.Scan(); }
        catch { return Array.Empty<DiscoveredPlugin>(); }
    }

    private static (long Bytes, int Files) DirSize(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return (0, 0);
        try
        {
            long b = 0; int n = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { b += new FileInfo(f).Length; n++; } catch { }
            }
            return (b, n);
        }
        catch { return (0, 0); }
    }

    private static string FormatBytes(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0.#} KB";
        return $"{b / (1024.0 * 1024.0):0.##} MB";
    }

    private static string FormatRelative(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return "gerade eben";
        if (d.TotalMinutes < 60) return $"vor {(int)d.TotalMinutes} min";
        if (d.TotalHours < 24) return $"vor {(int)d.TotalHours} h";
        return $"vor {(int)d.TotalDays} Tagen";
    }
}

public sealed record PluginHealthRow(
    string Id,
    string DisplayName,
    string Version,
    string MinHostVersion,
    int TargetGamesCount,
    string BundleSizeLabel,
    int BundleFileCount,
    string InstallSource,
    string UpdateCheckLabel);
