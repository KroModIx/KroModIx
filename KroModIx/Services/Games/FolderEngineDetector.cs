using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;

namespace KroModIx.Services.Games;

/// <summary>Erkennt bekannte Spiel-Engines in einem beliebigen Root-Ordner.
/// Aktuell nur Ren'Py — pro Engine ein <see cref="EngineMatch"/>.
///
/// <para><b>Ren'Py-Signature:</b> ein Container hat einen direkten
/// <c>game/</c>-Unterordner (Ren'Py-Universal-Marker seit v6). Ausserdem
/// wird die RenPack-Sub-Path-Rotation berücksichtigt: der Root kann
/// Container-Ordner mit Version-Sub-Ordnern enthalten (z. B.
/// <c>Game-0.240-pc/game/</c>).</para>
///
/// <para><b>Scan-Tiefe:</b> max 2 Ebenen unter Root (Root/Container/
/// SubOrdner/game). Tiefer ist bei realen Ren'Py-Sammlungen nicht
/// nötig und würde bei großen Ordnern zu lange dauern.</para></summary>
public sealed class FolderEngineDetector
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Scannt den Root und liefert alle erkannten Engines.
    /// Aktuell nur Ren'Py — bei Fund landet ein Match in der Liste, sonst leer.</summary>
    public IReadOnlyList<EngineMatch> Detect(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<EngineMatch>();

        var results = new List<EngineMatch>();
        var renpy = DetectRenpy(root);
        if (renpy.ContainerCount > 0) results.Add(renpy);
        return results;
    }

    private static EngineMatch DetectRenpy(string root)
    {
        int containers = 0;
        var samples = new List<string>();

        // (1) Root selbst hat game/ → Root ist ein einzelnes Ren'Py-Spiel.
        if (HasGameFolder(root))
        {
            containers = 1;
            samples.Add(Path.GetFileName(root));
        }
        else
        {
            // (2) Root/Container/ hat game/ → Container ist Ren'Py-Spiel (Legacy)
            // (3) Root/Container/SubVersion/ hat game/ → Sub-Path-Rotation (RenPack)
            foreach (var container in SafeEnumerateDirectories(root))
            {
                if (HasGameFolder(container)
                    || SafeEnumerateDirectories(container).Any(HasGameFolder))
                {
                    containers++;
                    if (samples.Count < 5) samples.Add(Path.GetFileName(container));
                }
            }
        }

        return new EngineMatch(
            Engine: "renpy",
            DisplayName: "Ren'Py",
            ContainerCount: containers,
            Samples: samples);
    }

    private static bool HasGameFolder(string dir)
    {
        try { return Directory.Exists(Path.Combine(dir, "game")); }
        catch { return false; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch (Exception ex)
        {
            Log.Debug(ex, "Enumerate fehlgeschlagen: {Dir}", dir);
            return Array.Empty<string>();
        }
    }
}

/// <summary>Ergebnis einer Engine-Detection in einem Root-Ordner.</summary>
/// <param name="Engine">Interner Engine-Slug (aktuell nur <c>renpy</c>).</param>
/// <param name="DisplayName">Menschenlesbarer Engine-Name.</param>
/// <param name="ContainerCount">Wie viele Container-Ordner mit dieser Engine gefunden wurden.</param>
/// <param name="Samples">Die ersten paar gefundenen Container-Namen für die UI.</param>
public sealed record EngineMatch(
    string Engine,
    string DisplayName,
    int ContainerCount,
    IReadOnlyList<string> Samples);
