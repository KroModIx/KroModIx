using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Plugins;

namespace KroModIx.ViewModels;

/// <summary>v1.23.0: Backup-Fenster pro Spiel. Aggregiert Snapshots aus
/// allen geladenen Plugins fuer diese GameKey (steam:appid oder manual:id).
/// Restore/Delete direkt aus der Liste. Das Erstellen von Snapshots
/// bleibt bei den Plugins (die wissen welche Verzeichnisse relevant sind);
/// dieses Fenster ist rein fuer Verwaltung + Restore.</summary>
public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly IBackupService _backup;
    private readonly PluginActivator _activator;
    private readonly string _gameKey;
    private readonly string _gameName;

    public ObservableCollection<BackupRow> Rows { get; } = new();

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _busy;
    public string GameHeader => $"🗄  Backups fuer „{_gameName}\"";

    public BackupsViewModel(IBackupService backup, PluginActivator activator,
        string gameKey, string gameName)
    {
        _backup = backup;
        _activator = activator;
        _gameKey = gameKey;
        _gameName = gameName;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Busy = true;
        try
        {
            Rows.Clear();
            long totalBytes = 0;
            foreach (var lp in _activator.Loaded)
            {
                var snaps = await _backup.ListSnapshotsAsync(lp.Manifest.Id, _gameKey)
                    .ConfigureAwait(true);
                foreach (var s in snaps)
                {
                    totalBytes += s.ZipBytes;
                    Rows.Add(new BackupRow(s, lp.Manifest.DisplayName));
                }
            }
            var sorted = Rows.OrderByDescending(r => r.Snapshot.CreatedUtc).ToList();
            Rows.Clear();
            foreach (var r in sorted) Rows.Add(r);

            StatusMessage = Rows.Count == 0
                ? "Noch keine Snapshots. Plugins legen sie automatisch vor Install/Update an."
                : $"{Rows.Count} Snapshot(s) · {FormatBytes(totalBytes)} gesamt";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task RestoreAsync(BackupRow row)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var ok = await _backup.RestoreSnapshotAsync(row.Snapshot.Id).ConfigureAwait(true);
            StatusMessage = ok
                ? $"✅ „{row.Snapshot.Label}\" zurueckgespielt (vorheriger Zustand als .pre-restore- daneben gesichert)."
                : $"❌ Restore fehlgeschlagen — Details im Log.";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(BackupRow row)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var ok = await _backup.DeleteSnapshotAsync(row.Snapshot.Id).ConfigureAwait(true);
            if (ok) Rows.Remove(row);
            StatusMessage = ok ? $"🗑 „{row.Snapshot.Label}\" geloescht."
                               : "❌ Loeschen fehlgeschlagen.";
        }
        finally { Busy = false; }
    }

    private static string FormatBytes(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0.#} KB";
        return $"{b / (1024.0 * 1024.0):0.##} MB";
    }
}

public sealed partial class BackupRow : ObservableObject
{
    public BackupSnapshot Snapshot { get; }
    public string PluginDisplayName { get; }
    public BackupRow(BackupSnapshot snap, string pluginDisplayName)
    { Snapshot = snap; PluginDisplayName = pluginDisplayName; }

    public string DisplayLabel => Snapshot.Label;
    public string SubLine =>
        $"{PluginDisplayName} · {Snapshot.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {FormatBytes(Snapshot.ZipBytes)}";

    private static string FormatBytes(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0.#} KB";
        return $"{b / (1024.0 * 1024.0):0.##} MB";
    }
}
