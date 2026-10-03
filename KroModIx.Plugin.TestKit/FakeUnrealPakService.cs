using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.TestKit;

/// <summary>Attrappe für <see cref="IUnrealPakService"/>. Schreibt kein
/// echtes Pak, sondern eine Kennung plus JSON — das genügt für die Frage,
/// die ein Plugin-Test stellt.
///
/// <para><b>Warum keine echten Pak-Bytes.</b> Das Plugin verantwortet,
/// <b>welche</b> Tabelle gepatcht und <b>wohin</b> ein Eintrag sortiert
/// wird. Ob der Container danach byte-korrekt auf der Platte liegt, prüft
/// der Host (<c>HostUnrealPakServiceTests</c>, <c>RealUnrealPakTests</c>);
/// hier noch einmal mitzuprüfen würde die Plugin-Tests an ein Dateiformat
/// binden, das das Plugin nicht mehr verantwortet.</para></summary>
public class FakeUnrealPakService : IUnrealPakService
{
    /// <summary>Kennung am Dateianfang, damit <see cref="IsPakFile"/> ein
    /// Attrappen-Pak von allem anderen unterscheiden kann.</summary>
    private const string Marker = "FAKEPAK1";

    private sealed record Payload(string MountPoint, Dictionary<string, string> Entries);

    public bool IsPakFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var r = new StreamReader(path);
            var head = new char[Marker.Length];
            return r.Read(head, 0, head.Length) == head.Length
                   && new string(head) == Marker;
        }
        catch { return false; }
    }

    public IUnrealPakReader OpenRead(string path)
    {
        if (!IsPakFile(path))
            throw new UnsupportedPakFormatException($"Kein Attrappen-Pak: {path}");
        var json = File.ReadAllText(path)[Marker.Length..];
        var p = JsonSerializer.Deserialize<Payload>(json)
                ?? throw new UnsupportedPakFormatException($"Attrappen-Pak leer: {path}");
        return new Reader(p);
    }

    public IUnrealPakBuilder CreateBuilder(string? mountPoint = null)
        => new Builder(mountPoint ?? "../../../");

    private sealed class Reader(Payload p) : IUnrealPakReader
    {
        public string MountPoint => p.MountPoint;

        /// <summary>Ein stabiler Fingerabdruck über den Inhalt — dieselbe
        /// Rolle wie der echte Index-Hash, nur billiger gerechnet.</summary>
        public string IndexHash => Convert.ToHexStringLower(
            System.Security.Cryptography.SHA1.HashData(
                Encoding.UTF8.GetBytes(string.Join('\n',
                    p.Entries.OrderBy(e => e.Key, StringComparer.Ordinal)
                             .Select(e => e.Key + ":" + e.Value)))));

        public IReadOnlyList<UnrealPakEntry> Entries => p.Entries
            .Select(e => new UnrealPakEntry(e.Key, Convert.FromBase64String(e.Value).Length))
            .ToList();

        public bool Contains(string mountRelativePath) => p.Entries.ContainsKey(mountRelativePath);

        public byte[] Read(string mountRelativePath)
            => p.Entries.TryGetValue(mountRelativePath, out var b64)
                ? Convert.FromBase64String(b64)
                : throw new FileNotFoundException($"Im Pak nicht enthalten: {mountRelativePath}");

        public void Dispose() { }
    }

    private sealed class Builder(string mountPoint) : IUnrealPakBuilder
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public void Add(string mountPath, byte[] data)
        {
            var rel = mountPath.Replace('\\', '/').TrimStart('/');
            if (rel.Length == 0) throw new ArgumentException("Leerer Mount-Pfad.", nameof(mountPath));
            if (!_entries.TryAdd(rel, Convert.ToBase64String(data)))
                throw new InvalidOperationException($"Pfad doppelt im Pak: {rel}");
        }

        public bool Contains(string mountPath)
            => _entries.ContainsKey(mountPath.Replace('\\', '/').TrimStart('/'));

        public void Write(string targetPath)
        {
            if (_entries.Count == 0)
                throw new InvalidOperationException("Ein Pak ohne Einträge wäre sinnlos.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath,
                Marker + JsonSerializer.Serialize(new Payload(mountPoint, _entries)));
        }
    }
}
