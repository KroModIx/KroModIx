using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Archive;
using Xunit;

namespace KroModIx.Tests;

public sealed class HostArchiveServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kromodix-archive").FullName;
    private readonly IArchiveService _svc = new HostArchiveServiceImpl();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string MakeZip(string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, c) in entries)
        {
            var e = zip.CreateEntry(p);
            using var s = e.Open();
            using var w = new StreamWriter(s);
            w.Write(c);
        }
        return path;
    }

    [Fact]
    public void ListetEintraegeMitNormalisiertenPfaden()
    {
        var zip = MakeZip("a.zip",
            ("Mod/data.json", "{}"),
            (@"Mod\sub\x.lua", "-- lua"),
            ("README.txt", "lies mich"));

        var entries = _svc.List(zip);
        entries.Select(e => e.Path).Should().BeEquivalentTo(
            ["Mod/data.json", "Mod/sub/x.lua", "README.txt"]);
        entries.Single(e => e.Path == "README.txt").Size.Should().Be(9);
    }

    [Fact]
    public void PacktAusUndMeldetDieGeschriebenenDateien()
    {
        var zip = MakeZip("a.zip", ("Mod/data.json", "{}"), ("Mod/sub/x.lua", "-- lua"));
        var target = Path.Combine(_dir, "ziel");

        var result = _svc.Extract(zip, target);

        result.Count.Should().Be(2);
        result.SkippedUnsafe.Should().BeEmpty();
        File.Exists(Path.Combine(target, "Mod", "data.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, "Mod", "sub", "x.lua")).Should().BeTrue();
    }

    /// <summary>Das verbreitete Nexus-Layout: alles liegt in einem Ordner
    /// mit dem Mod-Namen, der beim Auspacken weg muss.</summary>
    [Fact]
    public void StripPrefixSchneidetDenHuellOrdnerAb()
    {
        var zip = MakeZip("a.zip",
            ("MeinMod/data.json", "{}"),
            ("MeinMod/sub/x.lua", "-- lua"),
            ("README.txt", "liegt daneben"));
        var target = Path.Combine(_dir, "ziel");

        var result = _svc.Extract(zip, target, new ArchiveExtractOptions(StripPrefix: "MeinMod"));

        result.Count.Should().Be(2, "die README liegt außerhalb des Präfix");
        File.Exists(Path.Combine(target, "data.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, "sub", "x.lua")).Should().BeTrue();
        File.Exists(Path.Combine(target, "README.txt")).Should().BeFalse();
    }

    [Fact]
    public void StripPrefixIgnoriertDieSchreibweiseUndDenSchraegstrich()
    {
        var zip = MakeZip("a.zip", ("MeinMod/data.json", "{}"));
        foreach (var prefix in new[] { "MeinMod", "MeinMod/", "meinmod", "/MeinMod/" })
        {
            var target = Path.Combine(_dir, "ziel-" + prefix.Replace('/', '_'));
            _svc.Extract(zip, target, new ArchiveExtractOptions(StripPrefix: prefix))
                .Count.Should().Be(1, prefix);
            File.Exists(Path.Combine(target, "data.json")).Should().BeTrue(prefix);
        }
    }

    /// <summary>Für Ziele, die nur eine Ebene lesen — Icarus'
    /// <c>Content/Paks/mods</c> etwa.</summary>
    [Fact]
    public void FlattenVerwirftDieOrdnerstruktur()
    {
        var zip = MakeZip("a.zip", ("tief/drin/A_P.pak", "pak"), ("B_P.pak", "pak"));
        var target = Path.Combine(_dir, "ziel");

        _svc.Extract(zip, target, new ArchiveExtractOptions(Flatten: true)).Count.Should().Be(2);

        Directory.GetFiles(target).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["A_P.pak", "B_P.pak"]);
    }

    [Fact]
    public void FilterWaehltEintraegeAus()
    {
        var zip = MakeZip("a.zip", ("A_P.pak", "pak"), ("README.txt", "x"));
        var target = Path.Combine(_dir, "ziel");

        _svc.Extract(zip, target, new ArchiveExtractOptions(
            Filter: p => p.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))).Count.Should().Be(1);

        File.Exists(Path.Combine(target, "A_P.pak")).Should().BeTrue();
        File.Exists(Path.Combine(target, "README.txt")).Should().BeFalse();
    }

    [Fact]
    public void OhneUeberschreibenWirftEinTreffer()
    {
        var zip = MakeZip("a.zip", ("x.json", "{}"));
        var target = Path.Combine(_dir, "ziel");
        _svc.Extract(zip, target).Count.Should().Be(1);

        var act = () => _svc.Extract(zip, target, new ArchiveExtractOptions(Overwrite: false));
        act.Should().Throw<IOException>();
    }

    [Fact]
    public void PacktEinenEinzelnenEintragAus()
    {
        var zip = MakeZip("a.zip", ("Extracted Mods/X.EXMOD", "{\"name\":\"X\"}"), ("README.txt", "x"));
        var dest = Path.Combine(_dir, "raus", "X.EXMOD");

        _svc.ExtractEntry(zip, "Extracted Mods/X.EXMOD", dest);

        File.ReadAllText(dest).Should().Be("{\"name\":\"X\"}");
        ((Action)(() => _svc.ExtractEntry(zip, "gibtsnicht", dest)))
            .Should().Throw<FileNotFoundException>();
    }

    // ---- Typ-Erkennung ----

    [Fact]
    public void ErkenntZipAmInhaltUndNichtAnDerEndung()
    {
        var zip = MakeZip("heisst-anders.pak", ("x.txt", "x"));
        _svc.DetectKind(zip).Should().Be(ArchiveKind.Zip);
    }

    [Theory]
    [InlineData(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }, ArchiveKind.Rar)]
    [InlineData(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00 }, ArchiveKind.SevenZip)]
    [InlineData(new byte[] { 0x48, 0x61, 0x6C, 0x6C, 0x6F, 0x21, 0x0A }, ArchiveKind.Unknown)]
    [InlineData(new byte[] { 0x00 }, ArchiveKind.Unknown)]
    public void ErkenntDieSignaturen(byte[] head, ArchiveKind expected)
    {
        var path = Path.Combine(_dir, "probe.bin");
        File.WriteAllBytes(path, head);
        _svc.DetectKind(path).Should().Be(expected);
    }

    [Fact]
    public void FehlendeDateiIstUnbekannt()
        => _svc.DetectKind(Path.Combine(_dir, "gibtsnicht.zip")).Should().Be(ArchiveKind.Unknown);

    [Theory]
    [InlineData("mod.zip", true)]
    [InlineData("mod.RAR", true)]
    [InlineData("mod.7z", true)]
    [InlineData("mod.pak", false)]
    [InlineData("mod.txt", false)]
    public void EndungsVorfilter(string name, bool expected)
        => _svc.HasSupportedExtension(name).Should().Be(expected);

    // ---- Zip-Slip ----

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData(@"..\..\windows\system32\evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\windows\evil.dll")]
    [InlineData("C:/windows/evil.dll")]
    [InlineData("harmlos/../../../raus.txt")]
    [InlineData("")]
    [InlineData("   ")]
    public void AusbruchsversuchWirdAbgelehnt(string relative)
    {
        var root = Path.Combine(_dir, "root");
        _svc.TryResolveSafe(root, relative, out var dst).Should().BeFalse(relative);
        dst.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Mod_P.pak")]
    [InlineData("UE4SS Mods/DepositoMinerios/Scripts/main.lua")]
    [InlineData(@"UE4SS Mods\DepositoMinerios\enabled.txt")]
    [InlineData("a/./b.txt")]
    [InlineData("a/sub/../b.txt")]
    public void NormalerPfadWirdAngenommen(string relative)
    {
        var root = Path.Combine(_dir, "root");
        _svc.TryResolveSafe(root, relative, out var dst).Should().BeTrue(relative);
        dst.Should().StartWith(Path.GetFullPath(root));
    }

    /// <summary>Ein Archiv mit Ausbruchsversuch wird nicht nur übersprungen,
    /// sondern gemeldet — der Aufrufer soll es sehen können. Ein stilles
    /// Weglassen wäre bei einem böswilligen Archiv die falsche
    /// Reaktion.</summary>
    [Fact]
    public void AusbruchsversuchImArchivWirdGemeldet()
    {
        var zip = Path.Combine(_dir, "boese.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            // ZipFile normalisiert "../" nicht — genau deshalb braucht es den
            // Schutz. Jeder Eintrags-Stream muss geschlossen sein, bevor der
            // naechste angelegt wird.
            using (var s = z.CreateEntry("../raus.txt").Open())
                s.Write(Encoding.UTF8.GetBytes("boese"));
            using (var s = z.CreateEntry("brav.txt").Open())
                s.Write(Encoding.UTF8.GetBytes("brav"));
        }
        var target = Path.Combine(_dir, "ziel");

        var result = _svc.Extract(zip, target);

        result.Count.Should().Be(1);
        result.SkippedUnsafe.Should().ContainSingle().Which.Should().Contain("raus.txt");
        File.Exists(Path.Combine(_dir, "raus.txt")).Should().BeFalse("nichts darf neben dem Ziel landen");
    }
}
