using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Default fuer Hosts &lt; v1.23.0 — no-op Snapshots. Ein leerer
/// <see cref="BackupSnapshot"/> wird zurueckgegeben, damit Plugin-Code
/// nicht auf null pruefen muss.</summary>
public sealed class NullBackupService : IBackupService
{
    public static readonly NullBackupService Instance = new();
    private NullBackupService() { }

    public Task<BackupSnapshot> CreateSnapshotAsync(
        string pluginId, string gameKey,
        IReadOnlyList<string> directories, string label,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new BackupSnapshot(
            Id: "",
            PluginId: pluginId,
            GameKey: gameKey,
            Label: label,
            CreatedUtc: DateTime.UtcNow,
            ZipBytes: 0,
            ZipPath: "",
            SourceDirectories: directories));

    public Task<IReadOnlyList<BackupSnapshot>> ListSnapshotsAsync(
        string pluginId, string gameKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BackupSnapshot>>(Array.Empty<BackupSnapshot>());

    public Task<bool> RestoreSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> DeleteSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<int> PruneAsync(string pluginId, string gameKey, int keepLast,
        CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
