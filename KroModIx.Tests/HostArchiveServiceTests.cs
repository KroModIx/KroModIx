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

    // ---- Tar-Varianten (v1.37) ----
    //
    // ArchiveFactory lehnt ein tar.bz2 mit "Cannot determine compressed stream
    // type" ab — nachgemessen am 18.10.2026. Deshalb der sequentielle
    // Reader-Weg; diese Tests halten fest, dass er sich nach aussen genauso
    // verhaelt wie der wahlfreie.

    /// <summary>Baut ein Tar ueber das System-tar — so entsteht ein Archiv,
    /// das wirklich aus der Welt kommt und nicht aus derselben Bibliothek,
    /// die es gleich lesen soll.</summary>
    private string MakeTar(string name, string tarFlag, params (string Path, string Content)[] entries)
    {
        var stage = Path.Combine(_dir, "stage-" + Guid.NewGuid().ToString("N"));
        foreach (var (p, c) in entries)
        {
            var full = Path.Combine(stage, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, c);
        }
        var archiv = Path.Combine(_dir, name);
        var psi = new System.Diagnostics.ProcessStartInfo("tar")
        {
            ArgumentList = { $"-c{tarFlag}f", archiv, "-C", stage, "." },
            RedirectStandardError = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException("tar: " + proc.StandardError.ReadToEnd());
        return archiv;
    }

    [Theory]
    [InlineData("j", "sdk.tar.bz2", ArchiveKind.TarBz2)]
    [InlineData("z", "sdk.tar.gz", ArchiveKind.TarGz)]
    [InlineData("", "sdk.tar", ArchiveKind.Tar)]
    public void Tar_Varianten_werden_am_Inhalt_erkannt(string flag, string name, ArchiveKind erwartet)
    {
        var a = MakeTar(name, flag, ("lib/py3-linux-x86_64/renpy", "bin"));
        _svc.DetectKind(a).Should().Be(erwartet);
    }

    [Fact]
    public void Tar_bz2_laesst_sich_auflisten()
    {
        var a = MakeTar("sdk.tar.bz2", "j",
            ("renpy.sh", "#!/bin/sh"),
            ("lib/py3-linux-x86_64/renpy", "bin"),
            ("lib/py3-windows-x86_64/renpy.exe", "exe"));

        var liste = _svc.List(a).Select(e => e.Path).ToList();

        liste.Should().HaveCount(3);
        liste.Should().Contain(p => p.EndsWith("lib/py3-linux-x86_64/renpy"));
    }

    [Fact]
    public void Tar_bz2_Filter_holt_nur_das_Gewuenschte()
    {
        // Der Anwendungsfall: aus einem 146-MB-SDK nur die Linux-Runtime
        // ziehen, statt alles auszupacken.
        var a = MakeTar("sdk.tar.bz2", "j",
            ("renpy.sh", "#!/bin/sh"),
            ("lib/py3-linux-x86_64/renpy", "bin"),
            ("lib/py3-windows-x86_64/renpy.exe", "exe"),
            ("doc/readme.txt", "egal"));
        var ziel = Path.Combine(_dir, "out");

        var res = _svc.Extract(a, ziel, new ArchiveExtractOptions(
            Filter: k => k.Contains("py3-linux-x86_64") || k.EndsWith("renpy.sh")));

        res.Count.Should().Be(2);
        res.SkippedUnsafe.Should().BeEmpty();
        Directory.Exists(Path.Combine(ziel, "doc")).Should().BeFalse();
    }

    [Fact]
    public void Tar_bz2_ExtractEntry_holt_eine_einzelne_Datei()
    {
        var a = MakeTar("sdk.tar.bz2", "j",
            ("renpy.sh", "#!/bin/sh\necho start"),
            ("lib/x/y", "egal"));
        var ziel = Path.Combine(_dir, "renpy.sh");

        _svc.ExtractEntry(a, "./renpy.sh", ziel);

        File.ReadAllText(ziel).Should().Contain("echo start");
    }

    [Fact]
    public void Tar_bz2_ExtractEntry_meldet_einen_fehlenden_Eintrag()
    {
        var a = MakeTar("sdk.tar.bz2", "j", ("renpy.sh", "x"));
        var act = () => _svc.ExtractEntry(a, "gibtsnicht.txt", Path.Combine(_dir, "x"));
        act.Should().Throw<FileNotFoundException>();
    }
}
