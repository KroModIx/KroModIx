using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KroModIx.Plugin.Contracts;

/// <summary>Findet den Mod-Ordner eines Spiels, statt ihn festzunageln — der
/// gemeinsame Baukasten für alle Plugins (Contracts v1.29+).
///
/// <para><b>Warum im Host-Contract:</b> jedes Plugin hatte seinen eigenen
/// Einzeiler der Form <c>Path.Combine(game.InstallDir, "Mods")</c>. Ein
/// einziger Kandidat, exakte Groß-/Kleinschreibung, kein Ausweichpfad und
/// kein Anlegen. Zwei Folgen davon, beide real aufgetreten:</para>
///
/// <list type="number">
/// <item><b>Der Ordner fehlt einfach.</b> Mod-Ordner wie
///   <c>FactoryGame/Mods</c> oder <c>archive/pc/mod</c> gehören nicht zur
///   Vanilla-Installation — sie entstehen erst durch den Mod-Loader oder die
///   erste Mod. Nach einer Neuinstallation sind sie weg. Die Plugins meldeten
///   dann „Mods-Ordner existiert nicht" und zeigten eine leere Liste, ohne
///   Weg zurück: installieren konnte man auch nichts, weil das Ziel
///   fehlte.</item>
/// <item><b>Groß-/Kleinschreibung.</b> Auf Windows egal, unter Linux nicht.
///   Ein von Hand oder von einem Loader angelegtes <c>mods/</c> wird von
///   <c>Directory.Exists(".../Mods")</c> schlicht nicht gefunden — das Spiel
///   lädt die Mods, KroModIx sieht sie nicht.</item>
/// </list>
///
/// <para><b>Was dieser Baukasten NICHT tut:</b> er sucht nicht frei im
/// Dateisystem nach irgendetwas, das nach Mods aussieht. Er bekommt vom
/// Plugin eine geordnete Liste bekannter Relativpfade und löst genau diese
/// auf. Ein Mod-Ordner ist ein Installationsziel — dort hineinzuraten wäre
/// teurer als ein fehlender Ordner.</para></summary>
public static class ModFolderDiscovery
{
    /// <summary>Löst einen Relativpfad unterhalb von <paramref name="root"/>
    /// segmentweise auf: erst exakt, bei Fehlschlag gegen die echten
    /// Verzeichniseinträge ohne Rücksicht auf Groß-/Kleinschreibung.
    /// Rückgabe ist der reale Pfad mit der Schreibweise auf der Platte, oder
    /// null wenn ein Segment nirgends passt.
    ///
    /// <para>Mehrdeutigkeit (<c>Mods/</c> UND <c>mods/</c> nebeneinander, auf
    /// einem case-sensitiven Dateisystem möglich) wird deterministisch
    /// aufgelöst: die exakte Schreibweise gewinnt, sonst die alphabetisch
    /// erste. Ohne diese Regel haengt das Ergebnis an der
    /// Verzeichnisreihenfolge des Dateisystems.</para></summary>
    public static string? ResolveCaseInsensitive(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        if (string.IsNullOrWhiteSpace(relativePath)) return root;

        var current = root;
        foreach (var segment in SplitSegments(relativePath))
        {
            var exact = Path.Combine(current, segment);
            if (Directory.Exists(exact)) { current = exact; continue; }

            string[] candidates;
            try
            {
                candidates = Directory.GetDirectories(current)
                    .Where(d => string.Equals(Path.GetFileName(d), segment,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception)
            {
                // Kein Lesezugriff auf diese Ebene — fuer den Aufrufer ist das
                // dasselbe wie „nicht gefunden"; er darf davon nicht crashen.
                return null;
            }
            if (candidates.Length == 0) return null;
            current = candidates[0];
        }
        return current;
    }

    /// <summary>Der erste Kandidat aus <paramref name="relativeCandidates"/>,
    /// der unter <paramref name="root"/> wirklich existiert — oder null.
    /// Legt nichts an. Für reine Scan-Pfade („welche Mods liegen da?").</summary>
    public static string? Find(string root, params string[] relativeCandidates)
        => FindAll(root, relativeCandidates).FirstOrDefault();

    /// <summary>Alle existierenden Kandidaten in der übergebenen Reihenfolge,
    /// dedupliziert. Für Plugins mit mehreren gleichwertigen Mod-Orten
    /// (Cyberpunk: Archive, REDmod, CET, RED4ext, redscript).</summary>
    public static IReadOnlyList<string> FindAll(string root, params string[] relativeCandidates)
    {
        var hits = new List<string>();
        if (relativeCandidates is null) return hits;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in relativeCandidates)
        {
            var resolved = ResolveCaseInsensitive(root, rel);
            if (resolved is not null && seen.Add(resolved)) hits.Add(resolved);
        }
        return hits;
    }

    /// <summary>Wie <see cref="Find"/>, legt aber den <b>ersten</b> Kandidaten
    /// an, wenn keiner existiert — der kanonische Pfad des Spiels. Für
    /// Installationsziele: ohne Ordner kein Install, und ein Mod-Ordner ist
    /// kein Eingriff ins Spiel, sondern genau der Ort, den auch der Mod-Loader
    /// anlegen würde.
    ///
    /// <para>Rückgabe ist der reale Pfad. Schlägt das Anlegen fehl (Rechte,
    /// read-only Mount), kommt null zurück statt einer Exception — der
    /// Aufrufer soll das melden, nicht daran sterben.</para></summary>
    public static string? FindOrCreate(string root, params string[] relativeCandidates)
    {
        var existing = Find(root, relativeCandidates);
        if (existing is not null) return existing;
        if (relativeCandidates is null || relativeCandidates.Length == 0) return null;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

        var canonical = Path.Combine(root,
            Path.Combine(SplitSegments(relativeCandidates[0]).ToArray()));
        try
        {
            Directory.CreateDirectory(canonical);
            return canonical;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Zerlegt einen Relativpfad in Segmente und akzeptiert dabei
    /// beide Trennzeichen — Plugin-Konstanten sind mal <c>"a/b"</c>, mal
    /// <c>"a\\b"</c> geschrieben.</summary>
    private static IEnumerable<string> SplitSegments(string relativePath)
        => relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
}
