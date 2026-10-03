using System;
using System.Collections.Generic;

namespace KroModIx.Plugin.Contracts;

/// <summary>Welches Archiv-Format eine Datei <b>wirklich</b> ist — bestimmt
/// an den Magic-Bytes, nicht an der Endung.</summary>
public enum ArchiveKind
{
    /// <summary>Kein erkanntes Archiv, oder die Datei ist nicht lesbar.</summary>
    Unknown,
    Zip,
    Rar,
    SevenZip,
}

/// <summary>Ein Eintrag in einem Archiv. <see cref="Path"/> ist
/// archiv-relativ und immer mit <c>/</c> getrennt, auch wenn das Archiv
/// Backslashes gespeichert hat.</summary>
public sealed record ArchiveEntry(string Path, long Size);

/// <summary>Wie <see cref="IArchiveService.Extract"/> auspacken soll.</summary>
/// <param name="StripPrefix">Dieses Präfix (archiv-relativ, mit oder ohne
/// abschließenden <c>/</c>) vom Pfad jedes Eintrags abschneiden; Einträge
/// außerhalb werden übersprungen. Für das verbreitete Nexus-Layout, bei dem
/// alles in einem Ordner mit dem Mod-Namen liegt.</param>
/// <param name="Filter">Nur Einträge auspacken, für die das Prädikat wahr
/// ist. Bekommt den archiv-relativen Pfad (vor
/// <paramref name="StripPrefix"/>).</param>
/// <param name="Flatten">Die Ordnerstruktur verwerfen und alle Einträge
/// direkt nach <c>targetDir</c> legen. Für Ziele, die nur eine Ebene lesen —
/// Icarus' <c>Content/Paks/mods</c> etwa.</param>
/// <param name="Overwrite">Vorhandene Dateien überschreiben. Bei
/// <c>false</c> wirft ein Treffer.</param>
public sealed record ArchiveExtractOptions(
    string? StripPrefix = null,
    Func<string, bool>? Filter = null,
    bool Flatten = false,
    bool Overwrite = true);

/// <summary>Was ein Auspack-Vorgang getan hat.</summary>
/// <param name="ExtractedPaths">Die geschriebenen Dateien, absolut.</param>
/// <param name="SkippedUnsafe">Einträge, die der Zip-Slip-Schutz abgelehnt
/// hat. Nicht leer heißt: das Archiv ist kaputt oder böswillig — der
/// Aufrufer soll das melden, nicht übergehen.</param>
public sealed record ArchiveExtractResult(
    IReadOnlyList<string> ExtractedPaths,
    IReadOnlyList<string> SkippedUnsafe)
{
    public int Count => ExtractedPaths.Count;
}

/// <summary>Zentraler Archiv-Baukasten (v1.30.0+): ZIP, RAR und 7z lesen und
/// sicher auspacken.
///
/// <para><b>Warum im Host.</b> Nachgemessen am 03.10.2026, vom Verbraucher
/// her statt über den Namen des Helfers: <b>neun</b> Plugins öffnen Archive,
/// <b>sechs</b> tragen eine eigene Kopie des Zip-Slip-Schutzes, und bei
/// <b>vier</b> davon hält diese Kopie nicht — sie prüft nur auf <c>..</c>.
/// Genau die Art von Code, bei der ein Fehler an einer Stelle gefunden und
/// an den anderen vergessen wird, und es ist ein Sicherheitsschutz. Der
/// Ausbruch ist am Cyberpunk-Installer nachgewiesen, siehe
/// <see cref="ArchivePathSafety"/>.</para>
///
/// <para><b>Was dieser Baukasten NICHT tut:</b> er weiß nichts über
/// Mod-Formate. Welcher Eintrag eines Archivs eine Mod ist und wohin sie
/// gehört, bleibt Sache des Plugins — das ist spielspezifisches Wissen. Der
/// Baukasten liefert die Einträge, den Typ der Datei und ein Auspacken, das
/// nicht aus dem Zielverzeichnis ausbricht.</para></summary>
public interface IArchiveService
{
    /// <summary>Die Endungen, die dieser Baukasten behandelt — als Vorfilter
    /// beim Durchsuchen eines Ordners. Die verbindliche Einordnung macht
    /// <see cref="DetectKind"/>.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    bool HasSupportedExtension(string path);

    /// <summary>Bestimmt das Format am <b>Inhalt</b>, nicht an der Endung.
    ///
    /// <para>Das ist keine Vorsichtsmaßnahme, sondern Pflicht: Download-Ordner
    /// enthalten Dateien mit falscher Endung (ein Werkzeug hat ein <c>.pak</c>
    /// angehängt, ein Browser ein <c>.zip</c>), und nach der Endung behandelt
    /// landen sie unverändert im Spiel, wo sie niemand lesen kann — still,
    /// ohne Fehler.</para></summary>
    ArchiveKind DetectKind(string path);

    /// <summary>Die Einträge eines Archivs, ohne etwas auszupacken.
    /// Verzeichnis-Einträge sind nicht enthalten.</summary>
    IReadOnlyList<ArchiveEntry> List(string archivePath);

    /// <summary>Packt ein Archiv nach <paramref name="targetDir"/> aus.
    /// Jeder Zielpfad wird gegen <paramref name="targetDir"/> aufgelöst und
    /// nur geschrieben, wenn er wirklich darunter landet.</summary>
    ArchiveExtractResult Extract(string archivePath, string targetDir,
        ArchiveExtractOptions? options = null);

    /// <summary>Packt einen einzelnen Eintrag an einen konkreten Zielpfad
    /// aus. Für den Fall, dass ein Plugin genau eine Datei aus einem Archiv
    /// braucht — ein Manifest etwa.</summary>
    void ExtractEntry(string archivePath, string entryPath, string destinationFile);

    /// <summary>Löst einen archiv-relativen Pfad gegen
    /// <paramref name="root"/> auf und nimmt ihn nur an, wenn das Ergebnis
    /// wirklich unterhalb von root landet.
    ///
    /// <para>Öffentlich, weil ein Plugin Pfade auch außerhalb von
    /// <see cref="Extract"/> zusammenbaut (ein Mod-Ordnername aus einem
    /// Manifest etwa) und dafür denselben Schutz braucht. Ein
    /// <c>Contains("..")</c>-Test reicht nicht: absolute Pfade und
    /// Laufwerksbuchstaben rutschen daran vorbei, und ob
    /// <c>C:\…</c> als absolut gilt, hängt an der Plattform.</para></summary>
    bool TryResolveSafe(string root, string relative, out string destination);
}

/// <summary>Default für Hosts &lt; v1.30.0. Lesende Abfragen antworten
/// neutral, jedes Auspacken scheitert laut — ein folgenlos „erfolgreicher"
/// Install hätte nichts installiert.</summary>
public sealed class NullArchiveService : IArchiveService
{
    public static readonly NullArchiveService Instance = new();
    private NullArchiveService() { }

    private const string Feature = "Archiv-Baukasten (IArchiveService)";
    private const string MinHost = "1.30.0";

    public IReadOnlyList<string> SupportedExtensions => Array.Empty<string>();
    public bool HasSupportedExtension(string path) => false;
    public ArchiveKind DetectKind(string path) => ArchiveKind.Unknown;
    public IReadOnlyList<ArchiveEntry> List(string archivePath) => Array.Empty<ArchiveEntry>();

    public ArchiveExtractResult Extract(string archivePath, string targetDir,
        ArchiveExtractOptions? options = null)
        => throw new HostFeatureUnavailableException(Feature, MinHost);

    public void ExtractEntry(string archivePath, string entryPath, string destinationFile)
        => throw new HostFeatureUnavailableException(Feature, MinHost);

    /// <summary>Antwortet seit v1.32.0 <b>richtig</b> statt
    /// <c>false</c> — über <see cref="ArchivePathSafety"/>.
    ///
    /// <para>Früher gab diese Stelle pauschal „nicht sicher" zurück, mit der
    /// Begründung, das sei die sichere Richtung für einen ausgefallenen
    /// Schutz. Das war falsch gedacht: der Schutz ist eine reine Rechnung
    /// und fällt nie aus. Ein Plugin, das auf einem Host &lt; v1.30.0 seine
    /// Zielpfade selbst zusammenbaut, bekam dadurch für <b>jeden</b> Pfad
    /// ein Nein und schrieb gar nichts — kein Schutz, sondern ein
    /// Totalausfall ohne Fehlermeldung. Auspacken scheitert weiter laut, das
    /// ist der Teil, der wirklich einen Host braucht.</para></summary>
    public bool TryResolveSafe(string root, string relative, out string destination)
        => ArchivePathSafety.TryResolve(root, relative, out destination);
}
