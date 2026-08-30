using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KroModIx.Plugin.Contracts;

/// <summary>Semantischer Vergleich von Mod-Versionsstrings — der gemeinsame
/// Baukasten fuer alle Plugins (Contracts v1.26+).
///
/// <para><b>Warum im Host-Contract:</b> vorher hatte jedes Plugin seine
/// eigene Variante, jede anders unvollstaendig. LS25 sogar zwei Kopien im
/// selben Repo. Alle bauten auf <see cref="Version"/>.TryParse und gaben bei
/// unparsebarem Format still <c>false</c> zurueck — eine Mod-Version wie
/// <c>3</c>, <c>1.2.3b</c> oder <c>2.0-hotfix</c> bekam damit NIE ein Update
/// gemeldet, ohne eine Zeile Log. Ren'Py verglich sogar nur auf
/// String-Ungleichheit und meldete ein Update, wenn die lokale Version
/// NEUER war als die im Katalog.</para>
///
/// <para><b>Regeln:</b> Zahlen-Segmente numerisch (<c>0.10 &gt; 0.9</c> —
/// lexikografisch waere es umgekehrt), fehlende Segmente zaehlen als 0
/// (<c>1.0</c> == <c>1.0.0</c>), <c>v</c>-Prefix/Klammern/Plattform-Suffixe
/// werden wegnormalisiert (<c>v0.5</c> == <c>[0.5]</c> == <c>0.5-pc</c>).
/// Ein Suffix entscheidet nur bei gleichem Zahlenteil: Hotfix-Buchstaben
/// machen neuer (<c>0.4.5b</c> &gt; <c>0.4.5a</c> &gt; <c>0.4.5</c>),
/// Pre-Release-Woerter aelter (<c>1.0 beta</c> &lt; <c>1.0</c>).</para>
///
/// <para><b>Nicht parsebar</b> (<c>Ep. 5</c>, <c>Final</c>) liefert
/// <c>null</c> aus <see cref="Compare"/> bzw. <c>false</c> aus
/// <see cref="IsNewer"/>. Plugins mit unsauberen Katalog-Versionen
/// (f95zone!) koennen darauf ihre eigene Fallback-Policy bauen, statt dass
/// der Baukasten raet.</para></summary>
public static class VersionCompare
{
    /// <summary>Ist <paramref name="candidate"/> echt neuer als
    /// <paramref name="installed"/>? Bei gleich, aelter, leer oder nicht
    /// vergleichbar: <c>false</c>.</summary>
    public static bool IsNewer(string? candidate, string? installed)
        => Compare(candidate, installed) is int cmp && cmp > 0;

    /// <summary>Vergleicht zwei Versionsstrings. &gt;0 = a neuer, 0 = gleich,
    /// &lt;0 = a aelter. <c>null</c> wenn mindestens eine Seite keinen
    /// numerischen Kern hat.</summary>
    public static int? Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        if (pa is null || pb is null) return null;

        var (numsA, suffixA) = pa.Value;
        var (numsB, suffixB) = pb.Value;
        int len = Math.Max(numsA.Count, numsB.Count);
        for (int i = 0; i < len; i++)
        {
            long va = i < numsA.Count ? numsA[i] : 0;
            long vb = i < numsB.Count ? numsB[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return CompareSuffix(suffixA, suffixB);
    }

    /// <summary>Comparer fuer Sortierungen (z. B. Versions-Sub-Ordner eines
    /// Containers). Faellt bei nicht-parsebaren Werten auf Ordinal-Vergleich
    /// zurueck, damit die Sortierung stabil bleibt.</summary>
    public static IComparer<string?> Comparer { get; } = new VersionComparer();

    private sealed class VersionComparer : IComparer<string?>
    {
        public int Compare(string? x, string? y)
            => VersionCompare.Compare(x, y)
               ?? string.Compare(Normalize(x), Normalize(y), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Vereinheitlicht Schreibweisen: Klammern/Whitespace weg,
    /// fuehrendes <c>v</c> weg, Plattform-/Final-Suffixe weg, lowercase.</summary>
    public static string Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var v = version.Trim().Trim('[', ']', '(', ')', '{', '}').Trim();
        if (v.Length > 1 && (v[0] == 'v' || v[0] == 'V') && char.IsDigit(v[1]))
            v = v[1..];
        v = v.ToLowerInvariant();
        v = PlatformSuffixRegex.Replace(v, "");
        return v.Trim();
    }

    private static readonly Regex PlatformSuffixRegex = new(
        @"[-_\s]+(pc|win|windows|linux|mac|osx|android|all|final|full|public|steam)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CoreRegex = new(
        @"^(\d+(?:\.\d+)*)[.\-_\s]*(.*)$",
        RegexOptions.Compiled);

    private static (List<long> Numbers, string Suffix)? Parse(string? version)
    {
        var norm = Normalize(version);
        if (norm.Length == 0) return null;
        var m = CoreRegex.Match(norm);
        if (!m.Success) return null;

        var numbers = new List<long>();
        foreach (var part in m.Groups[1].Value.Split('.'))
        {
            // Ueberlange Segmente (Datums-Builds o. ae.) nicht crashen lassen
            if (!long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return null;
            numbers.Add(n);
        }
        return (numbers, m.Groups[2].Value.Trim());
    }

    private static readonly string[] PreReleaseMarkers =
        ["alpha", "beta", "rc", "pre", "dev", "wip", "test", "demo"];

    private static int CompareSuffix(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return 0;
        int ra = SuffixRank(a), rb = SuffixRank(b);
        if (ra != rb) return ra.CompareTo(rb);
        return string.CompareOrdinal(a, b);
    }

    /// <summary>-1 = Pre-Release (aelter als die nackte Version), 0 = kein
    /// Suffix, +1 = Hotfix-Kennzeichnung (neuer).</summary>
    private static int SuffixRank(string suffix)
    {
        if (suffix.Length == 0) return 0;
        foreach (var marker in PreReleaseMarkers)
            if (suffix.StartsWith(marker, StringComparison.Ordinal)) return -1;
        return 1;
    }
}
