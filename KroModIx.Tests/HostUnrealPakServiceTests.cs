using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Pak;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Der Pak-Container ist die Stelle, an der ein Fehler nicht als
/// Ausnahme auffällt, sondern als „die Mod tut nichts": ein Pak mit falschem
/// Index lädt das Spiel stillschweigend nicht. Deshalb geht hier jedes
/// geschriebene Pak durch den eigenen Leser zurück.
///
/// <para>Zusätzlich ist der Baukasten gegen echte Daten gemessen — siehe
/// <see cref="RealUnrealPakTests"/>, das sich überspringt, wo kein
/// Unreal-Spiel installiert ist (also im CI).</para></summary>
public sealed class HostUnrealPakServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kromodix-pak").FullName;
    private readonly IUnrealPakService _svc = new HostUnrealPakServiceImpl();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private static Dictionary<string, byte[]> SamplePayloads() => new(StringComparer.Ordinal)
    {
        // Eine Datentabelle, ein Asset, eine Datei auf oberster Ebene und
        // etwas Binäres — die vier Formen, die im echten Betrieb vorkommen.
        ["data/Items/D_ItemsStatic.json"] = Encoding.UTF8.GetBytes("{\"Rows\":[{\"Name\":\"A\"}]}"),
        ["data/Traits/D_Energy.json"] = Encoding.UTF8.GetBytes("{\"Rows\":[]}"),
        ["Assets/2DArt/UI/Icon.uasset"] = RandomNumberGenerator.GetBytes(4096),
        ["wurzel.json"] = Encoding.UTF8.GetBytes("{}"),
    };

    [Fact]
    public void GeschriebenesPakKommtUnveraendertZurueck()
    {
        var payloads = SamplePayloads();
        var path = Path.Combine(_dir, "test_P.pak");

        var b = _svc.CreateBuilder("../../../Icarus/Content/");
        foreach (var (k, v) in payloads) b.Add(k, v);
        b.Count.Should().Be(payloads.Count);
        b.Write(path);

        using var r = _svc.OpenRead(path);
        r.MountPoint.Should().Be("../../../Icarus/Content/",
            "der Mount-Point kommt vom Aufrufer, nicht vom Baukasten");
        r.Entries.Select(e => e.Path).Should().BeEquivalentTo(payloads.Keys);
        foreach (var (k, v) in payloads)
        {
            r.Read(k).Should().Equal(v, $"{k} muss byte-gleich zurückkommen");
            r.Entries.Single(e => e.Path == k).Size.Should().Be(v.Length);
        }
    }

    [Fact]
    public void OhneMountPointGiltDieBlosseUe4Konvention()
    {
        var path = Path.Combine(_dir, "default_P.pak");
        var b = _svc.CreateBuilder();
        b.Add("data/X.json", Encoding.UTF8.GetBytes("{}"));
        b.Write(path);

        using var r = _svc.OpenRead(path);
        r.MountPoint.Should().Be("../../../");
    }

    /// <summary>Gleiche Eingabe, gleiche Bytes — unabhängig von der
    /// Aufrufreihenfolge. Das ist nicht Kosmetik: ohne diese Eigenschaft
    /// änderte sich das Pak bei jedem Neubau, und eine Prüfsumme darüber
    /// wäre als „hat sich etwas geändert"-Signal unbrauchbar.</summary>
    [Fact]
    public void AusgabeIstReproduzierbar()
    {
        var payloads = SamplePayloads();
        var a = Path.Combine(_dir, "a_P.pak");
        var z = Path.Combine(_dir, "z_P.pak");

        var b1 = _svc.CreateBuilder();
        foreach (var (k, v) in payloads) b1.Add(k, v);
        b1.Write(a);

        var b2 = _svc.CreateBuilder();
        foreach (var (k, v) in payloads.Reverse()) b2.Add(k, v);
        b2.Write(z);

        SHA256.HashData(File.ReadAllBytes(a)).Should().Equal(SHA256.HashData(File.ReadAllBytes(z)));
    }

    [Fact]
    public void IndexHashAendertSichMitDemInhalt()
    {
        var a = Path.Combine(_dir, "a_P.pak");
        var z = Path.Combine(_dir, "z_P.pak");

        var b1 = _svc.CreateBuilder();
        b1.Add("data/X.json", Encoding.UTF8.GetBytes("{\"Rows\":[]}"));
        b1.Write(a);

        var b2 = _svc.CreateBuilder();
        b2.Add("data/X.json", Encoding.UTF8.GetBytes("{\"Rows\":[{\"Name\":\"B\"}]}"));
        b2.Write(z);

        using var ra = _svc.OpenRead(a);
        using var rz = _svc.OpenRead(z);
        ra.IndexHash.Should().NotBe(rz.IndexHash);
        ra.IndexHash.Should().MatchRegex("^[0-9a-f]{40}$");
    }

    [Fact]
    public void PfadeWerdenNormalisiert()
    {
        var path = Path.Combine(_dir, "slash_P.pak");
        var b = _svc.CreateBuilder();
        b.Add("/wurzel.json", Encoding.UTF8.GetBytes("{}"));
        b.Add(@"data\Unter\X.json", Encoding.UTF8.GetBytes("{}"));
        b.Write(path);

        using var r = _svc.OpenRead(path);
        r.Entries.Select(e => e.Path).Should().BeEquivalentTo(["wurzel.json", "data/Unter/X.json"]);
    }

    [Fact]
    public void DoppelterPfadWirdAbgelehnt()
    {
        var b = _svc.CreateBuilder();
        b.Add("data/X.json", [1]);
        b.Contains("data/X.json").Should().BeTrue();
        var act = () => b.Add("/data/X.json", [2]);
        act.Should().Throw<InvalidOperationException>(
            "der führende Schrägstrich macht keinen neuen Pfad");
    }

    [Fact]
    public void LeeresPakWirdAbgelehnt()
    {
        var act = () => _svc.CreateBuilder().Write(Path.Combine(_dir, "leer_P.pak"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AbgebrochenerSchreibvorgangLaesstKeineZwischendateiZurueck()
    {
        var target = Path.Combine(_dir, "unterverzeichnis", "ziel_P.pak");
        var b = _svc.CreateBuilder();
        b.Add("data/X.json", Encoding.UTF8.GetBytes("{}"));
        b.Write(target);
        File.Exists(target).Should().BeTrue();
        File.Exists(target + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void FremdeDateiWirdAlsUnbekanntesFormatAbgelehnt()
    {
        var path = Path.Combine(_dir, "kein.pak");
        File.WriteAllText(path, "das ist kein Pak");
        _svc.IsPakFile(path).Should().BeFalse();
        var act = () => _svc.OpenRead(path);
        act.Should().Throw<UnsupportedPakFormatException>();
    }

    /// <summary>Die Erkennung geht über die Footer-Magic, nicht über die
    /// Endung — und erkennt deshalb auch ein Pak mit falschem Namen.</summary>
    [Fact]
    public void PakErkennungHaengtNichtAnDerEndung()
    {
        var path = Path.Combine(_dir, "heisst-anders.bin");
        var b = _svc.CreateBuilder();
        b.Add("data/X.json", Encoding.UTF8.GetBytes("{}"));
        b.Write(path);

        _svc.IsPakFile(path).Should().BeTrue();
        _svc.IsPakFile(Path.Combine(_dir, "gibtsnicht.pak")).Should().BeFalse();
    }

    /// <summary>Der Pfad-Hash ist das Rezept, an dem die Vorlage am längsten
    /// gearbeitet hat (FNV-1a 64 über UTF-16LE, Seed auf den Offset-Basiswert
    /// <b>addiert</b>, kleingeschrieben, ohne führenden Schrägstrich). Er wird
    /// hier gegen seine Eigenschaften geprüft, damit ein Umbau nicht still
    /// etwas anderes berechnet.</summary>
    [Fact]
    public void PfadHashIstStabilUndUnterscheidendeFaelleUnabhaengig()
    {
        const ulong seed = UnrealPakFormat.WriterSeed;
        var a = UnrealPakFormat.HashPath("data/Items/D_ItemsStatic.json", seed);

        UnrealPakFormat.HashPath("data/Items/D_ItemsStatic.json", seed).Should().Be(a);
        UnrealPakFormat.HashPath("DATA/items/d_itemsstatic.JSON", seed).Should().Be(a,
            "UE-Pfade sind unempfindlich gegen Schreibweise");
        UnrealPakFormat.HashPath("/data/Items/D_ItemsStatic.json", seed).Should().Be(a,
            "ein führender Schrägstrich gehört nicht in den Hash");
        UnrealPakFormat.HashPath("data/Items/D_ItemTemplate.json", seed).Should().NotBe(a);
        UnrealPakFormat.HashPath("data/Items/D_ItemsStatic.json", seed + 1).Should().NotBe(a);
    }

    [Theory]
    [InlineData("a/b/c.json", "a/b/", "c.json")]
    [InlineData("c.json", "/", "c.json")]
    [InlineData("a/c.json", "a/", "c.json")]
    public void PfadZerlegungTrifftDieVerzeichnisSchluessel(string rel, string dir, string file)
    {
        var (d, f) = UnrealPakFormat.SplitMountPath(rel);
        d.Should().Be(dir);
        f.Should().Be(file);
    }
}

/// <summary>Die Null-Implementierung für alte Hosts: lesende Abfragen
/// antworten neutral, Aktionen scheitern laut. Ein folgenlos
/// „erfolgreicher" Pak-Bau wäre im Spiel von „die Mod tut nichts" nicht zu
/// unterscheiden.</summary>
public sealed class NullHostServiceTests
{
    [Fact]
    public void NullUnrealPakServiceAntwortetNeutralUndWirftBeiAktionen()
    {
        var svc = NullUnrealPakService.Instance;
        svc.IsPakFile("/egal.pak").Should().BeFalse();
        ((Action)(() => svc.OpenRead("/egal.pak"))).Should()
            .Throw<HostFeatureUnavailableException>().Which.MinHostVersion.Should().Be("1.30.0");
        ((Action)(() => svc.CreateBuilder())).Should().Throw<HostFeatureUnavailableException>();
    }

    [Fact]
    public void NullArchiveServiceAntwortetNeutralUndWirftBeiAktionen()
    {
        var svc = NullArchiveService.Instance;
        svc.SupportedExtensions.Should().BeEmpty();
        svc.HasSupportedExtension("x.zip").Should().BeFalse();
        svc.DetectKind("x.zip").Should().Be(ArchiveKind.Unknown);
        svc.List("x.zip").Should().BeEmpty();
        // Seit v1.32.0 rechnet auch diese Stelle richtig, statt pauschal
        // abzulehnen: der Ausbruch-Schutz ist eine reine Rechnung
        // (ArchivePathSafety) und fällt nicht mit dem Host aus. Das alte
        // „im Zweifel nein" klang sicher, hieß aber für ein Plugin auf einem
        // alten Host: jeder Pfad abgelehnt, nichts geschrieben, keine
        // Meldung. Was wirklich einen Host braucht — das Auspacken — wirft
        // weiter.
        svc.TryResolveSafe("/root", "a.txt", out var ziel).Should().BeTrue();
        ziel.Should().Be(Path.Combine(Path.GetFullPath("/root"), "a.txt"));
        svc.TryResolveSafe("/root", "../ausbruch.txt", out _).Should().BeFalse();
        svc.TryResolveSafe("/root", "/etc/passwd", out _).Should().BeFalse(
            "ein absoluter Eintragsname enthält kein „..“ und ist genau der Ausbruch, "
            + "der am Cyberpunk-Installer nachgewiesen wurde");
        ((Action)(() => svc.Extract("x.zip", "/ziel"))).Should()
            .Throw<HostFeatureUnavailableException>();
        ((Action)(() => svc.ExtractEntry("x.zip", "a", "/ziel/a"))).Should()
            .Throw<HostFeatureUnavailableException>();
    }

    [Fact]
    public void NullWinePrefixServiceAntwortetNeutralUndWirftBeimSetzen()
    {
        var svc = NullWinePrefixService.Instance;
        svc.IsDllOverrideSet("/pfx", "dwmapi").Should().BeFalse();
        ((Action)(() => svc.EnsureDllOverride("/pfx", "dwmapi"))).Should()
            .Throw<HostFeatureUnavailableException>().Which.Feature.Should().Contain("Wine");
    }
}
