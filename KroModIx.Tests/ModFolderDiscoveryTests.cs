using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Mod-Ordner-Aufloesung. Ein Fehlgriff hier ist teuer in beide
/// Richtungen: nicht gefunden heisst „Plugin zeigt nichts", falsch gefunden
/// heisst „Mod landet im falschen Verzeichnis".</summary>
public sealed class ModFolderDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "modfolder-" + Guid.NewGuid().ToString("N"));

    public ModFolderDiscoveryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Dir(string rel)
    {
        var p = Path.Combine(_root, Path.Combine(rel.Split('/')));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Findet_den_exakt_geschriebenen_Pfad()
    {
        var expected = Dir("FactoryGame/Mods");
        ModFolderDiscovery.Find(_root, "FactoryGame/Mods").Should().Be(expected);
    }

    [Fact]
    public void Findet_den_Ordner_auch_bei_abweichender_Schreibweise()
    {
        // Unter Linux der reale Fall: ein Loader legt „mods" an, das Plugin
        // sucht „Mods" — Directory.Exists sagt nein, das Spiel laedt sie aber.
        var expected = Dir("FactoryGame/mods");
        ModFolderDiscovery.Find(_root, "FactoryGame/Mods").Should().Be(expected);
    }

    [Fact]
    public void Loest_jedes_Segment_einzeln_auf()
    {
        var expected = Dir("factorygame/MODS");
        ModFolderDiscovery.Find(_root, "FactoryGame/Mods").Should().Be(expected);
    }

    [Fact]
    public void Bei_zwei_Schreibweisen_gewinnt_die_exakte()
    {
        var exact = Dir("Mods");
        Dir("mods2");                    // andere Namen stoeren nicht
        var hit = ModFolderDiscovery.Find(_root, "Mods");
        hit.Should().Be(exact);
    }

    [Fact]
    public void Ohne_Treffer_kommt_null_und_es_wird_nichts_angelegt()
    {
        ModFolderDiscovery.Find(_root, "FactoryGame/Mods").Should().BeNull();
        Directory.Exists(Path.Combine(_root, "FactoryGame")).Should().BeFalse();
    }

    [Fact]
    public void FindOrCreate_legt_den_ersten_Kandidaten_an()
    {
        var created = ModFolderDiscovery.FindOrCreate(_root, "FactoryGame/Mods", "Mods");

        created.Should().Be(Path.Combine(_root, "FactoryGame", "Mods"));
        Directory.Exists(created!).Should().BeTrue();
    }

    [Fact]
    public void FindOrCreate_legt_nichts_an_wenn_eine_Variante_schon_da_ist()
    {
        var existing = Dir("FactoryGame/mods");

        ModFolderDiscovery.FindOrCreate(_root, "FactoryGame/Mods").Should().Be(existing);
        Directory.Exists(Path.Combine(_root, "FactoryGame", "Mods")).Should().BeFalse();
    }

    [Fact]
    public void FindOrCreate_bevorzugt_einen_spaeteren_Kandidaten_der_existiert()
    {
        // Reihenfolge ist Prioritaet fuers Anlegen, nicht fuers Finden:
        // existiert irgendein Kandidat, wird NICHTS angelegt.
        var existing = Dir("Mods");

        ModFolderDiscovery.FindOrCreate(_root, "FactoryGame/Mods", "Mods").Should().Be(existing);
        Directory.Exists(Path.Combine(_root, "FactoryGame")).Should().BeFalse();
    }

    [Fact]
    public void FindAll_liefert_alle_vorhandenen_Orte_in_Reihenfolge()
    {
        var archive = Dir("archive/pc/mod");
        Dir("r6/scripts");
        var hits = ModFolderDiscovery.FindAll(_root,
            "archive/pc/mod", "mods", "r6/scripts", "red4ext/plugins");

        hits.Should().HaveCount(2);
        hits[0].Should().Be(archive);
    }

    [Fact]
    public void FindAll_dedupliziert_Kandidaten_die_auf_dasselbe_zeigen()
    {
        Dir("Mods");
        ModFolderDiscovery.FindAll(_root, "Mods", "mods").Should().ContainSingle();
    }

    [Fact]
    public void Backslash_Schreibweise_wird_wie_Slash_behandelt()
    {
        var expected = Dir("archive/pc/mod");
        ModFolderDiscovery.Find(_root, @"archive\pc\mod").Should().Be(expected);
    }

    [Fact]
    public void Nicht_existierende_Wurzel_liefert_null_statt_Exception()
    {
        var missing = Path.Combine(_root, "gibtsnicht");
        ModFolderDiscovery.Find(missing, "Mods").Should().BeNull();
        ModFolderDiscovery.FindOrCreate(missing, "Mods").Should().BeNull();
        ModFolderDiscovery.FindAll(missing, "Mods").Should().BeEmpty();
    }

    [Fact]
    public void Eine_Datei_mit_dem_Ordnernamen_zaehlt_nicht_als_Treffer()
    {
        File.WriteAllText(Path.Combine(_root, "Mods"), "keine Ordner");
        ModFolderDiscovery.Find(_root, "Mods").Should().BeNull();
    }
}
