using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.TestKit;

/// <summary>Attrappe für <see cref="IArchiveService"/>: packt echte ZIPs
/// aus, damit ein Plugin-Test prüfen kann, wo seine Dateien landen.
///
/// <para><b>Was sie bewusst nicht kann:</b> RAR und 7z. Die bräuchten
/// SharpCompress, und ein Plugin-Test, der RAR auspackt, prüft die
/// Fremdbibliothek, nicht sein Plugin. Für die Einordnung reicht
/// <see cref="DetectKind"/> an den Magic-Bytes — die erkennt alle drei
/// Formate.</para>
///
/// <para><b>Der Ausbruch-Schutz ist nicht nachgebaut:</b> er kommt aus
/// <see cref="ArchivePathSafety"/>, also derselben Rechnung, die der Host
/// benutzt. Eine eigene Kopie hier wäre genau der Fall, in dem die
/// Plugin-Tests grün sind und das Spiel trotzdem überschrieben wird.</para></summary>
public class FakeArchiveService : IArchiveService
{
    private static readonly string[] Exts = [".zip", ".rar", ".7z"];

    /// <summary>Jeder Auspack-Vorgang, den dieser Dienst gesehen hat — um zu
    /// prüfen, mit welchen Optionen ein Plugin aufgerufen hat, nicht nur was
    /// hinterher auf der Platte liegt.</summary>
    public List<(string Archive, string TargetDir, ArchiveExtractOptions Options)> ExtractCalls { get; } = [];

    public IReadOnlyList<string> SupportedExtensions => Exts;

    public bool HasSupportedExtension(string path)
        => Exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public ArchiveKind DetectKind(string path)
    {
        try
        {
            if (!File.Exists(path)) return ArchiveKind.Unknown;
            using var fs = File.OpenRead(path);
            var head = new byte[6];
            var read = fs.Read(head, 0, head.Length);
            if (read >= 2 && head[0] == 0x50 && head[1] == 0x4B) return ArchiveKind.Zip;
            if (read >= 4 && head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72 && head[3] == 0x21)
                return ArchiveKind.Rar;
            if (read >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC
                && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C)
                return ArchiveKind.SevenZip;
            return ArchiveKind.Unknown;
        }
        catch { return ArchiveKind.Unknown; }
    }

    public IReadOnlyList<ArchiveEntry> List(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        return zip.Entries
            .Where(e => e.Name.Length > 0)
            .Select(e => new ArchiveEntry(e.FullName.Replace('\\', '/'), e.Length))
            .ToList();
    }

    public ArchiveExtractResult Extract(string archivePath, string targetDir,
        ArchiveExtractOptions? options = null)
    {
        var opts = options ?? new ArchiveExtractOptions();
        ExtractCalls.Add((archivePath, targetDir, opts));

        var prefix = string.IsNullOrEmpty(opts.StripPrefix)
            ? null : opts.StripPrefix!.Replace('\\', '/').Trim('/') + "/";
        var extracted = new List<string>();
        var skipped = new List<string>();

        Directory.CreateDirectory(targetDir);
        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var e in zip.Entries)
        {
            if (e.Name.Length == 0) continue;
            var key = e.FullName.Replace('\\', '/');
            if (opts.Filter is not null && !opts.Filter(key)) continue;

            var rel = key;
            if (prefix is not null)
            {
                if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                rel = rel[prefix.Length..];
            }
            if (opts.Flatten) rel = rel.Split('/')[^1];
            if (rel.Length == 0) continue;

            if (!ArchivePathSafety.TryResolve(targetDir, rel, out var dst))
            {
                skipped.Add(key);
                continue;
            }
            if (File.Exists(dst) && !opts.Overwrite)
                throw new IOException($"Datei existiert bereits: {dst}");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            e.ExtractToFile(dst, overwrite: true);
            extracted.Add(dst);
        }
        return new ArchiveExtractResult(extracted, skipped);
    }

    public void ExtractEntry(string archivePath, string entryPath, string destinationFile)
    {
        var wanted = entryPath.Replace('\\', '/');
        using var zip = ZipFile.OpenRead(archivePath);
        var e = zip.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/') == wanted)
                ?? throw new FileNotFoundException(
                    $"Im Archiv nicht enthalten: {entryPath}", archivePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        e.ExtractToFile(destinationFile, overwrite: true);
    }

    public bool TryResolveSafe(string root, string relative, out string destination)
        => ArchivePathSafety.TryResolve(root, relative, out destination);
}
