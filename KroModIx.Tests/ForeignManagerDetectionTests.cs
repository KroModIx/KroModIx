using System;
using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Erkennung von Dateien und Ordnern, die einem anderen Mod-Manager
/// gehören. Der Anlass ist bezahlt: am 04.10.2026 hat ein Klick im
/// Icarus-Plugin lmms zusammengeführtes Datentabellen-Pak entfernt und damit
/// lautlos eine Mod aus dem Spiel genommen — die Quelle lag unversehrt in
/// lmms Zwischenspeicher, nur der Verweis im Spielordner fehlte.</summary>
public sealed class ForeignManagerDetectionTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("fremd-erkennung").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string Datei(string name)
    {
        var p = Path.Combine(_tmp, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "inhalt");
        return p;
    }

    [Fact]
    public void Der_gemessene_Fall_lmms_Merged_Pak_am_Namen()
    {
        ForeignManagerDetection.IsForeignManaged(Datei("zzz_LMM_Merged_P.pak"), out var wer)
            .Should().BeTrue();
        wer.Should().Be("lmm");
    }

    /// <summary>Das verlässliche Merkmal: lmm verweist in seinen eigenen
    /// Zwischenspeicher. Trägt auch für Manager, deren Namensschema wir nicht
    /// kennen.</summary>
    [Theory]
    [InlineData("share/lmm/cache/x.pak", "lmm")]
    [InlineData("config/r2modman/profiles/Default/BepInEx/plugins/x.dll", "r2modman")]
    [InlineData("data/vortex/mods/x.archive", "Vortex")]
    [InlineData("irgendwo/anders/x.pak", ForeignManagerDetection.UnbekannterVerwalter)]
    public void Datei_Verweis_nennt_den_Verwalter(string zielPfad, string erwartet)
    {
        var ziel = Datei(zielPfad);
        var link = Path.Combine(_tmp, "HarmlosBenannt.pak");
        File.CreateSymbolicLink(link, ziel);

        ForeignManagerDetection.IsForeignManaged(link, out var wer).Should().BeTrue();
        wer.Should().Be(erwartet);
    }

    /// <summary>Manche Plugins verwalten Mod-<b>Ordner</b>, nicht Dateien —
    /// Satisfactory, DSP, 7DTD. Ein Ordner-Verweis muss darum genauso
    /// erkannt werden.</summary>
    [Fact]
    public void Ordner_Verweis_wird_auch_erkannt()
    {
        var ziel = Path.Combine(_tmp, "config", "r2modman", "profiles", "MeinMod");
        Directory.CreateDirectory(ziel);
        var link = Path.Combine(_tmp, "MeinMod");
        Directory.CreateSymbolicLink(link, ziel);

        ForeignManagerDetection.IsForeignManaged(link, out var wer).Should().BeTrue();
        wer.Should().Be("r2modman");
    }

    /// <summary>Gegenprobe, und die ist die wichtigere Hälfte: eine von Hand
    /// hineinkopierte Mod ist unsere und muss sich weiter verwalten
    /// lassen. Eine Sperre, die den Normalfall mitnimmt, wäre schlimmer als
    /// das Problem.</summary>
    [Theory]
    [InlineData("Ultimate Envirosuit_P.pak")]
    [InlineData("levelcap_252_500_P.pak")]
    [InlineData("MeinMod.dll")]
    [InlineData("FS25_Autodrive.zip")]
    public void Gewoehnliche_Datei_bleibt_unsere(string name)
    {
        ForeignManagerDetection.IsForeignManaged(Datei(name), out var wer).Should().BeFalse();
        wer.Should().BeEmpty();
    }

    [Fact]
    public void Gewoehnlicher_Ordner_bleibt_unser()
    {
        var d = Path.Combine(_tmp, "MeinModOrdner");
        Directory.CreateDirectory(d);
        ForeignManagerDetection.IsForeignManaged(d, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Leerer_Pfad_ist_kein_Treffer(string pfad)
        => ForeignManagerDetection.IsForeignManaged(pfad, out _).Should().BeFalse();

    [Fact]
    public void Fehlender_Pfad_gilt_als_unser()
        => ForeignManagerDetection.IsForeignManaged(
            Path.Combine(_tmp, "gibtsnicht.pak"), out _).Should().BeFalse();

    /// <summary>Ein Ordner-Verweis mit abschließendem Trennzeichen darf nicht
    /// am Namensmuster vorbeirutschen — <c>Path.GetFileName</c> gibt dort
    /// sonst einen leeren Namen zurück.</summary>
    [Fact]
    public void Abschliessendes_Trennzeichen_stoert_nicht()
    {
        var d = Path.Combine(_tmp, "zzz_LMM_Ordner");
        Directory.CreateDirectory(d);

        ForeignManagerDetection.IsForeignManaged(d + Path.DirectorySeparatorChar, out var wer)
            .Should().BeTrue();
        wer.Should().Be("lmm");
    }

    /// <summary>Die Meldung muss drei Dinge sagen: wer, was verloren geht,
    /// und wohin der Nutzer stattdessen greift. Das Fehlen genau dieser drei
    /// hat die Stunden gekostet.</summary>
    [Fact]
    public void Die_Meldung_nennt_Verwalter_Folge_und_Ausweg()
    {
        var t = ForeignManagerDetection.Meldung("zzz_LMM_Merged_P.pak", "lmm", "deinstallieren");

        t.Should().Contain("zzz_LMM_Merged_P.pak");
        t.Should().Contain("lmm");
        t.Should().Contain("deinstallieren");
        t.Should().Contain("verschwänden", "die Folge gehört benannt, nicht nur das Verbot");
        t.Should().Contain("Quellen", "und dass die Quellen liegen bleiben");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Verwalter_bleibt_die_Meldung_lesbar(string? wer)
    {
        var t = ForeignManagerDetection.Meldung("x.pak", wer, "umschalten");

        t.Should().Contain(ForeignManagerDetection.UnbekannterVerwalter);
        t.Should().NotContain("  ", "keine Lücke, wo der Name fehlt");
    }
}
