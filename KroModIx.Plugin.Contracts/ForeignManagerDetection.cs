using System;
using System.IO;

namespace KroModIx.Plugin.Contracts;

/// <summary>Erkennt Dateien und Ordner im Spielverzeichnis, die <b>einem
/// anderen Mod-Manager gehören</b> — als reine Funktion, damit es sie genau
/// <b>einmal</b> gibt.
///
/// <para><b>Warum in den Contracts und nicht als Dienst.</b> Wie
/// <see cref="ArchivePathSafety"/> ist das Rechnung plus ein Blick ins
/// Dateisystem: kein Zustand, keine Abhängigkeit, keine laufende Anwendung.
/// Ein <c>IHostServices</c>-Glied dafür hätte eine Null-Implementierung, eine
/// Einspritzung und eine <c>minHostVersion</c>-Frage nach sich gezogen, ohne
/// irgendetwas zu gewinnen.</para>
///
/// <para><b>Der bezahlte Anlass, 04.10.2026.</b> Im Icarus-Mods-Ordner lag
/// <c>zzz_LMM_Merged_P.pak</c>, das zusammengeführte Datentabellen-Pak von
/// <a href="https://github.com/DonovanMods/linux-mod-manager">lmm</a> — und
/// damit die halbe Funktion einer gerade installierten Mod. Das Plugin
/// listete es als gewöhnliche manuelle Mod. Ein Klick auf Deinstallieren, und
/// der Gegenstand war aus dem Spiel: nicht kaputt, nur weg. Die Quelle lag
/// unversehrt in lmms Zwischenspeicher, nur der Verweis im Spielordner
/// fehlte. Gesucht wurde der Fehler danach stundenlang im Spiel, in den
/// Datentabellen und im Mod-Loader.</para>
///
/// <para><b>Ein Spielordner ist kein Alleinbesitz.</b> Neben KroModIx liefern
/// dort lmm, r2modman, Vortex, SMM/ficsit und der Steam-Workshop aus. Was ein
/// anderer verwaltet, wird <b>gelistet</b> — der Nutzer soll sehen, was im
/// Spiel liegt — und <b>nicht verändert</b>.</para></summary>
public static class ForeignManagerDetection
{
    /// <summary>Namens-Präfixe, die einen fremden Verwalter verraten, wenn er
    /// kopiert statt zu verweisen. Nur Rückfallebene: das verlässliche
    /// Merkmal ist der Verweis.</summary>
    private static readonly (string Prefix, string Owner)[] NamensMuster =
    [
        ("zzz_LMM_", "lmm"),
    ];

    /// <summary>Marker im Ziel eines Verweises. Belegt ist nur lmm
    /// (nachgemessen am 04.10.2026); die anderen sind die verbreiteten
    /// Ablageorte ihrer Werkzeuge. Trifft keiner, bleibt die Angabe neutral —
    /// das ist kein Mangel: für die Entscheidung „anfassen oder nicht" zählt
    /// <b>dass</b> es fremd ist, der Name nur für die Meldung.</summary>
    private static readonly (string Marker, string Owner)[] ZielMuster =
    [
        ("/lmm/", "lmm"),
        ("/r2modman/", "r2modman"),
        ("/thunderstore/", "Thunderstore"),
        ("/gale/", "Gale"),
        ("/vortex/", "Vortex"),
        ("/ficsit/", "SMM/ficsit"),
        ("/smm/", "SMM/ficsit"),
    ];

    /// <summary>Gehört <paramref name="path"/> einem fremden Mod-Manager?
    /// Funktioniert für Dateien <b>und</b> Ordner — manche Plugins verwalten
    /// Mod-Ordner, nicht einzelne Dateien.</summary>
    /// <param name="owner">Der erkannte Verwalter, oder
    /// <see cref="UnbekannterVerwalter"/>, wenn nur klar ist, <i>dass</i> es
    /// ein fremder ist. Leer, wenn die Antwort <c>false</c> lautet.</param>
    public static bool IsForeignManaged(string path, out string owner)
    {
        owner = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, '/'));
            foreach (var (prefix, besitzer) in NamensMuster)
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    owner = besitzer;
                    return true;
                }

            // Verweise auf Dateien UND auf Ordner — manche Plugins
            // verwalten Mod-Ordner. Die beiden Aufrufe werfen fuer den
            // jeweils anderen Typ, deshalb einzeln.
            var ziel = LinkZiel(path);
            if (ziel is not null)
            {
                owner = VerwalterAusZiel(ziel);
                return true;
            }
        }
        catch (Exception)
        {
            // Ein Lesefehler darf die Liste nicht umdeuten: dann gilt der
            // Eintrag als unser eigener und verhaelt sich wie bisher.
        }
        return false;
    }

    /// <summary>Das Ziel eines Verweises, oder null. <b>Bekannte Grenze:</b>
    /// <b>Hardlinks</b> werden damit nicht erkannt — .NET gibt die Zahl der
    /// Verweise auf eine Inode nicht heraus, und ein Hardlink ist vom
    /// Original nicht zu unterscheiden. Werkzeuge, die hardlinken statt zu
    /// verweisen, rutschen also durch; dort trägt nur das Namensmuster.
    /// Lieber benannt als stillschweigend übersehen.</summary>
    private static string? LinkZiel(string path)
    {
        try
        {
            if (File.Exists(path))
                return File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception) { /* kein Datei-Verweis — weiter mit dem Ordner */ }
        try
        {
            if (Directory.Exists(path))
                return Directory.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception) { /* auch kein Ordner-Verweis */ }
        return null;
    }

    /// <summary>Bezeichnung, wenn ein fremder Verwalter erkannt, aber nicht
    /// benannt werden konnte.</summary>
    public const string UnbekannterVerwalter = "ein anderer Mod-Manager";

    /// <summary>Die Meldung für den Nutzer. Sie braucht <b>drei</b> Angaben,
    /// und das Fehlen genau dieser drei hat am 04.10.2026 Stunden gekostet:
    /// <b>wer</b> es verwaltet, <b>was</b> beim Entfernen verloren ginge, und
    /// <b>wo</b> der Nutzer stattdessen hingreift. Eine Fehlermeldung ohne
    /// Ausweg ist eine halbe.</summary>
    /// <param name="displayName">Name der Datei oder des Ordners, wie er in
    /// der Liste steht.</param>
    /// <param name="owner">Der Verwalter aus
    /// <see cref="IsForeignManaged"/>.</param>
    /// <param name="verb">Was nicht geht, als Infinitiv —
    /// „deinstallieren", „umschalten".</param>
    public static string Meldung(string displayName, string? owner, string verb)
    {
        var wer = string.IsNullOrWhiteSpace(owner) ? UnbekannterVerwalter : owner!;
        return $"„{displayName}“ wird von {wer} verwaltet und lässt sich hier nicht {verb}. "
             + $"Darin stecken alle Mods, die {wer} dort ausgeliefert hat — sie verschwänden "
             + "auf einen Schlag aus dem Spiel, während ihre Quellen unberührt liegen bleiben. "
             + $"Zum Ändern {wer} benutzen.";
    }

    /// <summary>Leitet den Verwalter aus dem Ziel des Verweises ab. Trifft
    /// kein Marker, bleibt es neutral — raten wäre schlimmer als nicht
    /// wissen.</summary>
    private static string VerwalterAusZiel(string linkTarget)
    {
        var ziel = linkTarget.Replace('\\', '/');
        foreach (var (marker, besitzer) in ZielMuster)
            if (ziel.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return besitzer;
        return UnbekannterVerwalter;
    }
}
