using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Plugins;
using NLog;

namespace KroModIx.Services.Conflicts;

/// <summary>v1.24.0: Host-Impl von <see cref="IConflictScanner"/>.
/// Fragt alle geladenen Plugins die <see cref="IConflictSource"/>
/// implementieren, aggregiert die File-Karten und findet Dateien
/// mit mehr als einem Owner.
///
/// <para>Path-Vergleich case-insensitive (Windows-Filesysteme case-insensitive,
/// Linux mixed) und normalisiert auf '/' (Plugins koennten mit '\\' liefern).
/// Fehler einzelner Plugins blockieren nicht — sie landen im Log und die
/// Scan-Runde geht weiter.</para></summary>
public sealed class HostConflictScannerImpl : IConflictScanner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly PluginActivator _activator;

    public HostConflictScannerImpl(PluginActivator activator)
    {
        _activator = activator;
    }

    public async Task<IReadOnlyList<FileConflict>> ScanAsync(
        string gameKey, CancellationToken cancellationToken = default)
    {
        // Key = normalisierter relativer Pfad. Value = alle Owner die ihn beanspruchen.
        var byPath = new Dictionary<string, List<ConflictOwner>>(StringComparer.OrdinalIgnoreCase);

        foreach (var lp in _activator.Loaded)
        {
            if (lp.Plugin is not IConflictSource src) continue;
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ModFileset> filesets;
            try
            {
                filesets = await src.GetOwnedFilesAsync(gameKey, cancellationToken)
                    .ConfigureAwait(false) ?? Array.Empty<ModFileset>();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "ConflictSource {Plugin} warf bei GetOwnedFilesAsync", lp.Manifest.Id);
                continue;
            }
            foreach (var fs in filesets)
            foreach (var raw in fs.RelativeFiles)
            {
                var norm = Normalize(raw);
                if (norm.Length == 0) continue;
                if (!byPath.TryGetValue(norm, out var owners))
                    byPath[norm] = owners = new List<ConflictOwner>();
                owners.Add(new ConflictOwner(
                    PluginId: lp.Manifest.Id,
                    PluginDisplayName: lp.Manifest.DisplayName,
                    ModId: fs.ModId,
                    ModDisplayName: fs.ModDisplayName));
            }
        }

        return byPath
            .Where(kv => kv.Value.Count > 1)
            .OrderBy(kv => kv.Key)
            .Select(kv => new FileConflict(kv.Key, kv.Value))
            .ToList();
    }

    private static string Normalize(string s)
        => s.Trim().Replace('\\', '/').TrimStart('/');
}
