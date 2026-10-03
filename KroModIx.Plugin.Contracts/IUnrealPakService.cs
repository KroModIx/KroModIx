using System;
using System.Collections.Generic;

namespace KroModIx.Plugin.Contracts;

/// <summary>Wird geworfen, wenn ein Pak eine Eigenschaft nutzt, die der Host
/// bewusst nicht unterstützt (Verschlüsselung, Oodle-Kompression, eine zu
/// alte Pak-Version) — im Unterschied zu einem echten Lesefehler. Aufrufer
/// sollen daran laut scheitern, nicht stillschweigend etwas Halbes
/// tun.</summary>
public sealed class UnsupportedPakFormatException(string message) : Exception(message);

/// <summary>Ein Eintrag in einem Pak. <see cref="Path"/> ist mount-relativ,
/// etwa <c>Items/D_ItemsStatic.json</c>. <see cref="Size"/> ist die
/// <b>entpackte</b> Größe.</summary>
public sealed record UnrealPakEntry(string Path, long Size);

/// <summary>Lesezugriff auf ein geöffnetes Pak.</summary>
public interface IUnrealPakReader : IDisposable
{
    /// <summary>Der Mount-Point aus dem Primärindex — wo die Engine die
    /// mount-relativen Pfade einhängt. Echte Paks unterscheiden sich hier
    /// stark: eine vom Spiel ausgelieferte <c>data.pak</c> trägt den
    /// absoluten Pfad der Cook-Maschine, ein Mod-Pak eine relative Form.</summary>
    string MountPoint { get; }

    /// <summary>Die im Footer vermerkte SHA1 des Primärindex als
    /// Kleinbuchstaben-Hex — ein billiger, stabiler Fingerabdruck des
    /// Pak-Inhalts.
    ///
    /// <para>Nützlich als „hat sich dieses Pak geändert"-Signal: der Wert
    /// steht im Footer, wird beim Öffnen ohnehin gelesen und geprüft, und
    /// ändert sich bei jeder Inhalts- oder Layout-Änderung. Eine Prüfsumme
    /// über die (oft mehrere Gigabyte große) Datei zu rechnen wäre Arbeit
    /// ohne Gegenwert.</para></summary>
    string IndexHash { get; }

    IReadOnlyList<UnrealPakEntry> Entries { get; }

    bool Contains(string mountRelativePath);

    /// <summary>Die entpackten Bytes eines Eintrags.</summary>
    /// <exception cref="UnsupportedPakFormatException">Der Eintrag nutzt ein
    /// Kompressionsverfahren, das der Host nicht entpacken kann (Oodle).</exception>
    byte[] Read(string mountRelativePath);
}

/// <summary>Baut ein neues Pak. Einträge werden gepuffert,
/// <see cref="Write"/> gibt alles sortiert aus.</summary>
public interface IUnrealPakBuilder
{
    /// <summary>Nimmt einen Eintrag auf. Ein führender <c>/</c> und
    /// Backslashes werden normalisiert; derselbe Pfad zweimal ist ein
    /// Fehler.</summary>
    void Add(string mountPath, byte[] data);

    bool Contains(string mountPath);

    int Count { get; }

    /// <summary>Schreibt das Pak. Atomar über eine Zwischendatei — ein
    /// abgebrochener Lauf darf kein halbes Pak im Mod-Ordner hinterlassen,
    /// das das Spiel beim nächsten Start zu laden versucht.</summary>
    void Write(string targetPath);
}

/// <summary>Zentraler Unreal-Pak-Baukasten (v1.30.0+): Paks der UE4-Reihe
/// lesen und schreiben.
///
/// <para><b>Warum im Host.</b> Der Container ist Unreal, nicht ein
/// bestimmtes Spiel — jedes Plugin für ein UE-Spiel braucht ihn identisch.
/// Es gibt dafür auch nichts von der Stange: CUE4Parse liest nur, und zum
/// <b>Schreiben</b> findet sich auf NuGet keine Bibliothek. Diese
/// Implementierung ist aus <see href="https://github.com/DonovanMods/go-unrealpak">
/// go-unrealpak</see> portiert (MIT, Donovan C. Young), und zwar nicht aus
/// einer Spezifikation, sondern aus einer Implementierung, deren Werte gegen
/// eine echte Installation verifiziert wurden.</para>
///
/// <para><b>Was dieser Baukasten NICHT tut:</b> er kennt kein Spiel. Der
/// Mount-Point, unter dem ein Mod-Pak eingehängt werden muss, und die Pfade,
/// an denen ein Spiel seine Daten erwartet, sind spielspezifisch und kommen
/// vom Plugin (siehe <see cref="CreateBuilder"/>). Ein Baukasten, der
/// „Icarus" wüsste, wäre für Satisfactory falsch.</para>
///
/// <para><b>Grenzen, die absichtlich bestehen:</b> gelesen werden
/// unverschlüsselte Paks ab Version 10 mit gespeicherten oder
/// Zlib-komprimierten Einträgen. Oodle wird nicht entpackt — dafür bräuchte
/// es die proprietäre Bibliothek. Geschrieben wird Version 11,
/// unkomprimiert. Alles andere ist ein lauter
/// <see cref="UnsupportedPakFormatException"/> und kein stiller
/// Ausweichpfad.</para></summary>
public interface IUnrealPakService
{
    /// <summary>Ob die Datei ein Unreal-Pak ist — geprüft an der Magic im
    /// Footer, nicht an der Endung.
    ///
    /// <para>Die Reihenfolge ist wichtig, wenn ein Plugin auch Archive
    /// erkennt: diese Prüfung zuerst, danach
    /// <see cref="IArchiveService.DetectKind"/>. Umgekehrt wäre es
    /// angreifbar — ein Pak beginnt mit den Daten seiner ersten Datei, und
    /// die können zufällig mit <c>PK</c> anfangen, also wie ein ZIP
    /// aussehen.</para></summary>
    bool IsPakFile(string path);

    /// <summary>Öffnet ein Pak zum Lesen. Der Aufrufer entsorgt den
    /// Leser.</summary>
    /// <exception cref="UnsupportedPakFormatException">Keine Pak-Magic,
    /// Version zu alt, Index verschlüsselt.</exception>
    IUnrealPakReader OpenRead(string path);

    /// <summary>Beginnt ein neues Pak.</summary>
    /// <param name="mountPoint">Der Mount-Point, den das Pak deklariert —
    /// <b>spielspezifisch</b> und deshalb vom Aufrufer. Für Icarus etwa
    /// <c>../../../Icarus/Content/</c>. Ohne Angabe die bloße
    /// UE4-Konvention <c>../../../</c>, die vom Verzeichnis der Spiel-Exe
    /// zum äußeren Spielordner führt.</param>
    IUnrealPakBuilder CreateBuilder(string? mountPoint = null);
}

/// <summary>Default für Hosts &lt; v1.30.0. Lesende Abfragen antworten
/// neutral, Öffnen und Bauen scheitern laut — ein Pak, das folgenlos nicht
/// entsteht, wäre im Spiel von „die Mod tut nichts" nicht zu
/// unterscheiden.</summary>
public sealed class NullUnrealPakService : IUnrealPakService
{
    public static readonly NullUnrealPakService Instance = new();
    private NullUnrealPakService() { }

    private const string Feature = "Unreal-Pak-Baukasten (IUnrealPakService)";
    private const string MinHost = "1.30.0";

    public bool IsPakFile(string path) => false;

    public IUnrealPakReader OpenRead(string path)
        => throw new HostFeatureUnavailableException(Feature, MinHost);

    public IUnrealPakBuilder CreateBuilder(string? mountPoint = null)
        => throw new HostFeatureUnavailableException(Feature, MinHost);
}
