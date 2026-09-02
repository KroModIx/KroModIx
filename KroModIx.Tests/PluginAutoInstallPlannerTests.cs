using System;
using System.Linq;
using FluentAssertions;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using Xunit;

namespace KroModIx.Tests;

public class PluginAutoInstallPlannerTests
{
    private static PluginIndexEntry Entry(
        string id, int[]? appIds = null, string[]? engines = null,
        string? repo = "KroModIx/Some.Plugin", string kind = "github")
        => new()
        {
            Id = id,
            DisplayName = id,
            SteamAppIds = (appIds ?? Array.Empty<int>()).ToList(),
            Engines = (engines ?? Array.Empty<string>()).ToList(),
            UpdateSource = repo is null ? null : new PluginIndexUpdateSource { Kind = kind, Repo = repo },
        };

    private static PluginIndex Index(params PluginIndexEntry[] e) => new() { Plugins = e.ToList() };

    private static DiscoveredGame Steam(int appId)
        => new($"steam:{appId}", $"Game{appId}", "/games/x", appId, null, null,
            DiscoveredGameSource.Steam);

    private static DiscoveredGame RenPy(string name)
        => new($"manual:{name}", name, $"/games/{name}", null, name, null,
            DiscoveredGameSource.Manual, Engine: "renpy");

    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void Neuinstallation_plant_alle_Plugins_zu_vorhandenen_Spielen()
    {
        // Der Kernfall: Plugin-Ordner leer, Spiele aber noch in der Sidebar.
        var idx = Index(
            Entry("kroste.icarus", appIds: new[] { 1149460 }),
            Entry("kroste.renpyassist", engines: new[] { "renpy" }),
            Entry("kroste.ls25", appIds: new[] { 2300320 }));

        var plan = PluginAutoInstallPlanner.Plan(
            idx,
            new[] { Steam(1149460), RenPy("Happy Summer"), RenPy("Leaf Flow") },
            installedPluginIds: None, optedOutPluginIds: None, alreadyAttemptedIds: None);

        plan.Select(p => p.Id).Should().Equal("kroste.icarus", "kroste.renpyassist");
    }

    [Fact]
    public void Viele_RenPy_Kacheln_ergeben_genau_einen_Plan_Eintrag()
    {
        var idx = Index(Entry("kroste.renpyassist", engines: new[] { "renpy" }));
        var games = Enumerable.Range(0, 31).Select(i => RenPy($"Spiel {i}")).ToArray();

        PluginAutoInstallPlanner.Plan(idx, games, None, None, None)
            .Should().ContainSingle().Which.Id.Should().Be("kroste.renpyassist");
    }

    [Fact]
    public void Bereits_installierte_Plugins_werden_uebersprungen()
    {
        var idx = Index(Entry("kroste.renpyassist", engines: new[] { "renpy" }));
        PluginAutoInstallPlanner.Plan(idx, new[] { RenPy("X") },
            installedPluginIds: new[] { "KROSTE.RENPYASSIST" },
            optedOutPluginIds: None, alreadyAttemptedIds: None)
            .Should().BeEmpty();
    }

    [Fact]
    public void Vom_User_deinstallierte_Plugins_werden_nicht_nachgezogen()
    {
        var idx = Index(Entry("kroste.renpyassist", engines: new[] { "renpy" }));
        PluginAutoInstallPlanner.Plan(idx, new[] { RenPy("X") },
            installedPluginIds: None,
            optedOutPluginIds: new[] { "kroste.renpyassist" },
            alreadyAttemptedIds: None)
            .Should().BeEmpty();
    }

    [Fact]
    public void Gescheiterte_Versuche_bleiben_bis_zum_Cooldown_Ende_gesperrt()
    {
        var idx = Index(Entry("kroste.renpyassist", engines: new[] { "renpy" }));
        PluginAutoInstallPlanner.Plan(idx, new[] { RenPy("X") },
            None, None, alreadyAttemptedIds: new[] { "kroste.renpyassist" })
            .Should().BeEmpty();
    }

    [Fact]
    public void Plugins_ohne_passendes_Spiel_werden_nie_geholt()
    {
        // Kein „auf Vorrat laden" — das war die Bedingung dafuer, dass der
        // Auto-Install ueberhaupt ungefragt laufen darf.
        var idx = Index(
            Entry("kroste.cyberpunk2077", appIds: new[] { 1091500 }),
            Entry("kroste.renpyassist", engines: new[] { "renpy" }));

        PluginAutoInstallPlanner.Plan(idx, new[] { Steam(999999) }, None, None, None)
            .Should().BeEmpty();
    }

    [Fact]
    public void Eintraege_ohne_brauchbare_GitHub_Quelle_fallen_raus()
    {
        var idx = Index(
            Entry("ohne.source", engines: new[] { "renpy" }, repo: null),
            Entry("leeres.repo", engines: new[] { "renpy" }, repo: "   "),
            Entry("fremde.quelle", engines: new[] { "renpy" }, repo: "x/y", kind: "gitlab"));

        PluginAutoInstallPlanner.Plan(idx, new[] { RenPy("X") }, None, None, None)
            .Should().BeEmpty();
    }

    [Fact]
    public void Ohne_Spiele_oder_ohne_Index_passiert_nichts()
    {
        var idx = Index(Entry("kroste.renpyassist", engines: new[] { "renpy" }));
        PluginAutoInstallPlanner.Plan(idx, Array.Empty<DiscoveredGame>(), None, None, None)
            .Should().BeEmpty();
        PluginAutoInstallPlanner.Plan(null, new[] { RenPy("X") }, None, None, None)
            .Should().BeEmpty();
        PluginAutoInstallPlanner.Plan(new PluginIndex(), new[] { RenPy("X") }, None, None, None)
            .Should().BeEmpty();
    }
}
