using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace KroModIx.Services.Archive;

/// <summary>Host-Implementierung von <see cref="IArchiveService"/>.
///
/// <para>Gebaut aus dem, was vorher in den Plugins lag — der
/// Zip-Slip-Schutz kommt wörtlich aus <c>DspZipInstaller</c> und
/// <c>IcarusArchive</c>, die beide dieselbe Kopie trugen. Er lehnt
/// zusätzlich Laufwerksbuchstaben ab, und das ist nicht Paranoia: unter
/// Linux gilt <c>C:\windows\evil.dll</c> nicht als absoluter Pfad, der
/// Eintrag landete dort also als Ordner namens <c>C:</c> unterhalb des
/// Ziels, während Windows ihn ablehnt. Ausgebrochen wäre nichts, aber die
/// beiden Plattformen verhielten sich unterschiedlich — gefunden beim
/// Testen der Icarus-Fassung am 03.10.2026.</para></summary>
public sealed class HostArchiveServiceImpl : IArchiveService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly string[] Extensions = [".zip", ".rar", ".7z"];

    public IReadOnlyList<string> SupportedExtensions => Extensions;

    public bool HasSupportedExtension(string path)
        => Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public ArchiveKind DetectKind(string path)
    {
        try
        {
            if (!File.Exists(path)) return ArchiveKind.Unknown;
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[6];
            var read = fs.Read(head);
            // ZIP: "PK" + 03 04 / 05 06 (leer) / 07 08 (gespannt)
            if (read >= 2 && head[0] == 0x50 && head[1] == 0x4B) return ArchiveKind.Zip;
            // RAR: "Rar!" 1A 07
            if (read >= 4 && head[0] == 0x52 && head[1] == 0x61
                && head[2] == 0x72 && head[3] == 0x21) return ArchiveKind.Rar;
            // 7z: "7z" BC AF 27 1C
            if (read >= 4 && head[0] == 0x37 && head[1] == 0x7A
                && head[2] == 0xBC && head[3] == 0xAF) return ArchiveKind.SevenZip;
            // v1.37: Tar-Varianten. BZip2 "BZh" und GZip 1F 8B stehen fuer
            // sich; ob darunter wirklich ein Tar liegt, zeigt erst der
            // Reader. Das ist in Ordnung — die Alternative waere, 146 MB zu
            // dekomprimieren, nur um das Format zu benennen.
            if (read >= 3 && head[0] == 0x42 && head[1] == 0x5A && head[2] == 0x68)
                return ArchiveKind.TarBz2;
            if (read >= 2 && head[0] == 0x1F && head[1] == 0x8B)
                return ArchiveKind.TarGz;
            // Unkomprimiertes Tar: "ustar" ab Offset 257.
            if (IsPlainTar(fs)) return ArchiveKind.Tar;
            return ArchiveKind.Unknown;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Archiv-Erkennung fehlgeschlagen: {Path}", path);
            return ArchiveKind.Unknown;
        }
    }

    /// <summary>„ustar" ab Offset 257 — der Tar-Header hat keine Magic-Bytes
    /// am Anfang.</summary>
    private static bool IsPlainTar(FileStream fs)
    {
        try
        {
            if (fs.Length < 265) return false;
            fs.Position = 257;
            Span<byte> magic = stackalloc byte[5];
            if (fs.Read(magic) < 5) return false;
            return magic[0] == 0x75 && magic[1] == 0x73 && magic[2] == 0x74
                && magic[3] == 0x61 && magic[4] == 0x72;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Tar-Varianten koennen nicht wahlfrei gelesen werden:
    /// ArchiveFactory lehnt ein tar.bz2 mit „Cannot determine compressed
    /// stream type" ab (nachgemessen 18.10.2026). SharpCompress' Reader-API
    /// laeuft stattdessen sequentiell durch — fuer Auflisten und Auspacken
    /// reicht ein Durchlauf.</summary>
    private static bool IsSequentialOnly(ArchiveKind kind)
        => kind is ArchiveKind.Tar or ArchiveKind.TarGz or ArchiveKind.TarBz2;

    public IReadOnlyList<ArchiveEntry> List(string archivePath)
    {
        if (IsSequentialOnly(DetectKind(archivePath)))
        {
            var seq = new List<ArchiveEntry>();
            using var fs = File.OpenRead(archivePath);
            using var reader = ReaderFactory.Open(fs);
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || string.IsNullOrEmpty(reader.Entry.Key)) continue;
                seq.Add(new ArchiveEntry(Normalize(reader.Entry.Key!), reader.Entry.Size));
            }
            return seq;
        }

        using var archive = ArchiveFactory.Open(archivePath);
        return archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
            .Select(e => new ArchiveEntry(Normalize(e.Key!), e.Size))
            .ToList();
    }

    public ArchiveExtractResult Extract(string archivePath, string targetDir,
        ArchiveExtractOptions? options = null)
    {
        var opts = options ?? new ArchiveExtractOptions();
        var prefix = NormalizePrefix(opts.StripPrefix);
        var extracted = new List<string>();
        var skipped = new List<string>();

        Directory.CreateDirectory(targetDir);

        if (IsSequentialOnly(DetectKind(archivePath)))
        {
            using var fs = File.OpenRead(archivePath);
            using var reader = ReaderFactory.Open(fs);
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || string.IsNullOrEmpty(reader.Entry.Key)) continue;
                var ziel = PlanEntry(Normalize(reader.Entry.Key!), prefix, opts,
                    targetDir, archivePath, skipped);
                if (ziel is null) continue;
                if (File.Exists(ziel) && !opts.Overwrite)
                    throw new IOException($"Datei existiert bereits: {ziel}");
                Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);
                using (var outStream = File.Create(ziel))
                    reader.WriteEntryTo(outStream);
                extracted.Add(ziel);
            }
            return new ArchiveExtractResult(extracted, skipped);
        }

        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;
            var key = Normalize(entry.Key!);
            if (opts.Filter is not null && !opts.Filter(key)) continue;

            var relative = key;
            if (prefix is not null)
            {
                if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // Eintrag ausserhalb des Praefix — etwa eine README neben
                    // dem Mod-Ordner. Kein Fehler, nur nicht gemeint.
                    Log.Debug("Eintrag außerhalb des Präfix übersprungen: {Key}", key);
                    continue;
                }
                relative = relative[prefix.Length..];
            }
            if (opts.Flatten) relative = relative.Split('/')[^1];
            if (relative.Length == 0) continue;

            if (!TryResolveSafe(targetDir, relative, out var destination))
            {
                Log.Warn("Zip-Slip abgelehnt: {Key} (Archiv {Archive})", key, archivePath);
                skipped.Add(key);
                continue;
            }
            if (File.Exists(destination) && !opts.Overwrite)
                throw new IOException($"Datei existiert bereits: {destination}");

            WriteEntry(entry, destination);
            extracted.Add(destination);
        }
        return new ArchiveExtractResult(extracted, skipped);
    }

    /// <summary>Entscheidet fuer einen Archiv-Eintrag, wohin er geschrieben
    /// wird — oder dass er uebersprungen wird. Gemeinsam fuer beide Lesewege,
    /// damit Filter, Praefix, Flatten und der Zip-Slip-Schutz nicht in zwei
    /// Fassungen auseinanderlaufen. Rueckgabe null = nicht schreiben;
    /// abgelehnte Ausbruchsversuche landen in <paramref name="skipped"/>.</summary>
    private static string? PlanEntry(string key, string? prefix,
        ArchiveExtractOptions opts, string targetDir, string archivePath, List<string> skipped)
    {
        if (opts.Filter is not null && !opts.Filter(key)) return null;

        var relative = key;
        if (prefix is not null)
        {
            if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("Eintrag außerhalb des Präfix übersprungen: {Key}", key);
                return null;
            }
            relative = relative[prefix.Length..];
        }
        if (opts.Flatten) relative = relative.Split('/')[^1];
        if (relative.Length == 0) return null;

        if (!ArchivePathSafety.TryResolve(targetDir, relative, out var destination))
        {
            Log.Warn("Zip-Slip abgelehnt: {Key} (Archiv {Archive})", key, archivePath);
            skipped.Add(key);
            return null;
        }
        return destination;
    }

    public void ExtractEntry(string archivePath, string entryPath, string destinationFile)
    {
        var wanted = Normalize(entryPath);

        if (IsSequentialOnly(DetectKind(archivePath)))
        {
            using var fs = File.OpenRead(archivePath);
            using var reader = ReaderFactory.Open(fs);
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || string.IsNullOrEmpty(reader.Entry.Key)) continue;
                if (Normalize(reader.Entry.Key!) != wanted) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                using var outStream = File.Create(destinationFile);
                reader.WriteEntryTo(outStream);
                return;
            }
            throw new FileNotFoundException(
                $"Im Archiv nicht enthalten: {entryPath}", archivePath);
        }

        using var archive = ArchiveFactory.Open(archivePath);
        var entry = archive.Entries.FirstOrDefault(e =>
                        !e.IsDirectory && Normalize(e.Key ?? "") == wanted)
                    ?? throw new FileNotFoundException(
                        $"Im Archiv nicht enthalten: {entryPath}", archivePath);
        WriteEntry(entry, destinationFile);
    }

    /// <summary>Delegiert an <see cref="ArchivePathSafety"/> — der
    /// Algorithmus liegt seit v1.32.0 in den Contracts, damit Host,
    /// Test-Attrappen und Plugins, die Pfade selbst zusammenbauen, nicht
    /// drei Kopien davon pflegen.</summary>
    public bool TryResolveSafe(string root, string relative, out string destination)
        => ArchivePathSafety.TryResolve(root, relative, out destination);

    private static void WriteEntry(IArchiveEntry entry, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var input = entry.OpenEntryStream();
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    private static string Normalize(string key) => key.Replace('\\', '/');

    /// <summary>Das Präfix auf die Form <c>abc/</c> bringen — oder null,
    /// wenn keines gewünscht ist.</summary>
    private static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        var p = Normalize(prefix).Trim('/');
        return p.Length == 0 ? null : p + "/";
    }
}
