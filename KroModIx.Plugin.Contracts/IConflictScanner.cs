using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Zentraler Konflikt-Scanner (v1.24.0+). Findet Dateien, die
/// von mehr als einem installierten Mod beansprucht werden — der Klassiker
/// „Mod A und Mod B ueberschreiben beide <c>Content/Items/Sword.pak</c>,
/// nur der lexikografisch letzte gewinnt". Fuer BepInEx/REDmod/CET/etc.
/// noch relevanter weil ein Overwrite eines fremden Plugin-Files stumm
/// das Verhalten aendern kann.
///
/// <para>Design „Pull, nicht Push": Plugins implementieren
/// <see cref="IConflictSource"/> und liefern on-demand ihre File-Karte.
/// Der Host cached NICHTS — bei jedem Scan wird frisch gefragt. Kein
/// Deployment-Diff-Tracking. Wenn ein Plugin <c>IConflictSource</c> nicht
/// implementiert, taucht es einfach nicht in der Konflikt-Liste auf
/// (kein Cross-Cutting-Zwang).</para></summary>
public interface IConflictScanner
{
    /// <summary>Fragt alle geladenen Plugins die <see cref="IConflictSource"/>
    /// implementieren nach ihren Mod-Filesets fuer <paramref name="gameKey"/>,
    /// aggregiert und liefert nur Files mit mehr als einem Owner.</summary>
    Task<IReadOnlyList<FileConflict>> ScanAsync(
        string gameKey, CancellationToken cancellationToken = default);
}

/// <summary>Plugin-seitige Quelle fuer den <see cref="IConflictScanner"/>.
/// Optional zu implementieren — Plugins die Mod-Files kennen (BepInEx-DLLs,
/// REDmods, PAKs, .archive etc.) exposen ihre File-Karte hier.</summary>
public interface IConflictSource
{
    /// <summary>Alle installierten Mods dieses Plugins fuer <paramref name="gameKey"/>
    /// mit den relativen Dateien die sie ins Spiel-Verzeichnis legen.
    /// Relative Pfade nutzen '/' als Separator (case-insensitive verglichen).</summary>
    Task<IReadOnlyList<ModFileset>> GetOwnedFilesAsync(
        string gameKey, CancellationToken cancellationToken = default);
}

/// <summary>Alle Files die ein einzelner Mod beansprucht.</summary>
public sealed record ModFileset(
    string ModId,
    string ModDisplayName,
    IReadOnlyList<string> RelativeFiles);

/// <summary>Ein Konflikt: eine Datei mit mehr als einem Owner. Der letzte
/// Owner in der Liste ist blindlings der „Gewinner" (Alphabet-Reihenfolge),
/// aber der User will das ggf. explizit steuern — dieser Contract macht
/// den Konflikt sichtbar, den Fix macht der User selbst (deaktivieren
/// oder Load-Order).</summary>
public sealed record FileConflict(
    string RelativePath,
    IReadOnlyList<ConflictOwner> Owners);

public sealed record ConflictOwner(
    string PluginId,
    string PluginDisplayName,
    string ModId,
    string ModDisplayName);
