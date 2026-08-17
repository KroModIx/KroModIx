using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Zentraler Backup-Baukasten (v1.23.0+). Plugins koennen vor
/// riskanten Aktionen (Install/Update/Uninstall) einen Snapshot der
/// betroffenen Mod-Verzeichnisse anfordern. Der Host packt sie als ZIP
/// unter <c>~/.local/share/KroModIx/backups/&lt;pluginId&gt;/&lt;timestamp&gt;.zip</c>.
///
/// <para>Rollback ist NICHT automatisch. Der Host bietet in der Sidebar
/// „Backups" pro Spiel — der User waehlt selbst welchen Snapshot er
/// zurueckspielen will. Grund: nach dem Snapshot koennte der User weitere
/// Aenderungen vorgenommen haben, ein Auto-Rollback ueber die Snapshot-
/// Grenze hinaus zerstoert dann bewusste Arbeit. Sicherheitsnetz statt
/// Automatismus.</para></summary>
public interface IBackupService
{
    /// <summary>Erstellt einen Snapshot aller angegebenen Verzeichnisse.
    /// Existiert ein Verzeichnis nicht, wird es uebersprungen (kein Fehler).
    /// Der <paramref name="label"/> beschreibt den Anlass fuer die spaetere
    /// Anzeige („Vor Install von XYZ v1.2", „Manuell" etc.).</summary>
    Task<BackupSnapshot> CreateSnapshotAsync(
        string pluginId,
        string gameKey,
        IReadOnlyList<string> directories,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Alle bekannten Snapshots fuer <paramref name="pluginId"/>
    /// + <paramref name="gameKey"/>, chronologisch neu → alt.</summary>
    Task<IReadOnlyList<BackupSnapshot>> ListSnapshotsAsync(
        string pluginId,
        string gameKey,
        CancellationToken cancellationToken = default);

    /// <summary>Spielt den Snapshot zurueck. Existiert das Ziel-Verzeichnis,
    /// wird es vor dem Extract in <c>&lt;dir&gt;.pre-restore/</c> umbenannt
    /// (Sicherheitsnetz — der User kann selbst wiederherstellen). Rueckgabe:
    /// true = restored, false = Snapshot nicht gefunden oder Extract-Fehler.</summary>
    Task<bool> RestoreSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>Loescht einen einzelnen Snapshot.</summary>
    Task<bool> DeleteSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>Behaelt die <paramref name="keepLast"/> juengsten Snapshots
    /// pro <paramref name="pluginId"/>+<paramref name="gameKey"/> und
    /// loescht den Rest. Von Plugins nach jedem CreateSnapshot als
    /// Aufraeum-Schritt aufzurufen (Standard: keepLast = 5).</summary>
    Task<int> PruneAsync(
        string pluginId,
        string gameKey,
        int keepLast,
        CancellationToken cancellationToken = default);
}

/// <summary>Ein persistierter Snapshot.</summary>
public sealed record BackupSnapshot(
    string Id,
    string PluginId,
    string GameKey,
    string Label,
    DateTime CreatedUtc,
    long ZipBytes,
    string ZipPath,
    IReadOnlyList<string> SourceDirectories);
