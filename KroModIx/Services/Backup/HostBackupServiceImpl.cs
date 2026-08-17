using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Backup;

/// <summary>v1.23.0: <see cref="IBackupService"/>-Host-Impl. Snapshots
/// liegen als ZIP unter <c>~/.local/share/KroModIx/backups/&lt;pluginId&gt;/</c>,
/// pro Snapshot ein <c>&lt;timestamp&gt;.zip</c> + <c>.json</c>-Metadatei
/// (mit Label, GameKey, Source-Directories fuer den Restore).
///
/// <para>Kein Auto-Rollback — der Contract dokumentiert warum. Restore
/// packt existierende Ziel-Verzeichnisse vor dem Extract in
/// <c>&lt;dir&gt;.pre-restore-&lt;timestamp&gt;/</c> um (Doppel-Sicherheitsnetz).</para></summary>
public sealed class HostBackupServiceImpl : IBackupService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HostBackupServiceImpl()
    {
        _root = AppPaths.BackupsRoot;
    }

    public async Task<BackupSnapshot> CreateSnapshotAsync(
        string pluginId, string gameKey,
        IReadOnlyList<string> directories, string label,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) throw new ArgumentException(nameof(pluginId));
        var pluginDir = Path.Combine(_root, SanitizeSegment(pluginId));
        Directory.CreateDirectory(pluginDir);

        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var id = $"{ts}-{Guid.NewGuid():N}".Substring(0, 22);
        var zipPath = Path.Combine(pluginDir, id + ".zip");
        var metaPath = Path.Combine(pluginDir, id + ".json");

        var existing = directories.Where(Directory.Exists).ToList();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var srcDir in existing)
                {
                    var srcFull = Path.GetFullPath(srcDir);
                    var basePrefix = new DirectoryInfo(srcFull).Name;
                    foreach (var file in Directory.EnumerateFiles(srcFull, "*",
                                 SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var rel = Path.GetRelativePath(srcFull, file);
                        var entryName = $"{basePrefix}/{rel.Replace('\\', '/')}";
                        try { zip.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest); }
                        catch (IOException ex) { Log.Debug(ex, "Backup: skip {File}", file); }
                    }
                }
            }
            var bytes = new FileInfo(zipPath).Length;
            var snapshot = new BackupSnapshot(
                Id: id,
                PluginId: pluginId,
                GameKey: gameKey,
                Label: label,
                CreatedUtc: DateTime.UtcNow,
                ZipBytes: bytes,
                ZipPath: zipPath,
                SourceDirectories: existing);
            await File.WriteAllTextAsync(metaPath,
                JsonSerializer.Serialize(snapshot, JsonOpts), cancellationToken)
                .ConfigureAwait(false);
            Log.Info("Backup {Id} erstellt: {Bytes} bytes, {Dirs} Verzeichnisse ({Label})",
                id, bytes, existing.Count, label);
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    public Task<IReadOnlyList<BackupSnapshot>> ListSnapshotsAsync(
        string pluginId, string gameKey, CancellationToken cancellationToken = default)
    {
        var pluginDir = Path.Combine(_root, SanitizeSegment(pluginId));
        if (!Directory.Exists(pluginDir))
            return Task.FromResult<IReadOnlyList<BackupSnapshot>>(Array.Empty<BackupSnapshot>());
        var list = new List<BackupSnapshot>();
        foreach (var meta in Directory.EnumerateFiles(pluginDir, "*.json"))
        {
            try
            {
                var snap = JsonSerializer.Deserialize<BackupSnapshot>(File.ReadAllText(meta));
                if (snap is not null && string.Equals(snap.GameKey, gameKey, StringComparison.OrdinalIgnoreCase))
                    list.Add(snap);
            }
            catch (Exception ex) { Log.Debug(ex, "Backup-Meta-Read fehlgeschlagen: {Path}", meta); }
        }
        list.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
        return Task.FromResult<IReadOnlyList<BackupSnapshot>>(list);
    }

    public async Task<bool> RestoreSnapshotAsync(string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var meta = FindMetaById(snapshotId);
        if (meta is null) return false;
        BackupSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<BackupSnapshot>(await File.ReadAllTextAsync(meta.Value.MetaPath, cancellationToken)); }
        catch (Exception ex) { Log.Warn(ex, "Restore: Meta lesen fehlgeschlagen"); return false; }
        if (snap is null || !File.Exists(snap.ZipPath)) return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var zip = ZipFile.OpenRead(snap.ZipPath);
            var byPrefix = zip.Entries.GroupBy(e =>
            {
                var slash = e.FullName.IndexOf('/');
                return slash < 0 ? e.FullName : e.FullName.Substring(0, slash);
            }).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var srcDir in snap.SourceDirectories)
            {
                var srcFull = Path.GetFullPath(srcDir);
                var basePrefix = new DirectoryInfo(srcFull).Name;
                if (!byPrefix.TryGetValue(basePrefix, out var entries)) continue;

                if (Directory.Exists(srcFull))
                {
                    var safety = srcFull + $".pre-restore-{DateTime.UtcNow:yyyyMMddHHmmss}";
                    Directory.Move(srcFull, safety);
                    Log.Info("Restore: existierendes {Dir} nach {Safety} umbenannt", srcFull, safety);
                }
                Directory.CreateDirectory(srcFull);
                foreach (var entry in entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    var rel = entry.FullName.Substring(basePrefix.Length + 1);
                    var target = Path.Combine(srcFull, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            Log.Info("Restore {Id} erfolgreich", snapshotId);
            return true;
        }
        catch (Exception ex) { Log.Warn(ex, "Restore {Id} fehlgeschlagen", snapshotId); return false; }
        finally { _gate.Release(); }
    }

    public Task<bool> DeleteSnapshotAsync(string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var meta = FindMetaById(snapshotId);
        if (meta is null) return Task.FromResult(false);
        try
        {
            if (File.Exists(meta.Value.ZipPath)) File.Delete(meta.Value.ZipPath);
            File.Delete(meta.Value.MetaPath);
            Log.Info("Snapshot {Id} geloescht", snapshotId);
            return Task.FromResult(true);
        }
        catch (Exception ex) { Log.Warn(ex, "Delete-Snapshot fehlgeschlagen"); return Task.FromResult(false); }
    }

    public async Task<int> PruneAsync(string pluginId, string gameKey, int keepLast,
        CancellationToken cancellationToken = default)
    {
        var snaps = await ListSnapshotsAsync(pluginId, gameKey, cancellationToken)
            .ConfigureAwait(false);
        if (snaps.Count <= keepLast) return 0;
        int pruned = 0;
        foreach (var s in snaps.Skip(keepLast))
            if (await DeleteSnapshotAsync(s.Id, cancellationToken).ConfigureAwait(false))
                pruned++;
        return pruned;
    }

    private (string MetaPath, string ZipPath)? FindMetaById(string id)
    {
        if (!Directory.Exists(_root)) return null;
        foreach (var pluginDir in Directory.EnumerateDirectories(_root))
        {
            var meta = Path.Combine(pluginDir, id + ".json");
            if (File.Exists(meta))
                return (meta, Path.Combine(pluginDir, id + ".zip"));
        }
        return null;
    }

    private static string SanitizeSegment(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
