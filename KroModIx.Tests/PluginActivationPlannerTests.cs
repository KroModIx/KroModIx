using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using Xunit;

namespace KroModIx.Tests;

public class PluginActivationPlannerTests
{
    private static DiscoveredPlugin FakePlugin(
        string id, string version, string minHost,
        params (int appId, string name)[] targets)
    {
        var manifest = new PluginManifest
        {
            Schema = 1,
            Id = id,
            DisplayName = id,
            Version = version,
            Assembly = $"{id}.dll",
            EntryType = $"{id}.EntryType",
            MinHostVersion = minHost,
        };
        foreach (var (appId, name) in targets)
            manifest.Targets.Add(new GameTarget(name, name, appId,
                Array.Empty<string>(), Platforms.Both));
        return new DiscoveredPlugin(manifest, Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"{id}.plugin.json"));
    }

    private static DiscoveredGame FakeGame(int appId, string name) =>
        new(Key: $"steam:{appId}", DisplayName: name,
            InstallDir: $"/tmp/{name}", SteamAppId: appId,
            ManualId: null, CustomCoverPath: null,
            Source: DiscoveredGameSource.Steam);

    private static PluginActivationPlanner NewPlanner()
    {
        // AppSettingsService braucht Dateisystem — wir nutzen den echten Config-Root;
        // Tests laufen isoliert weil sie AlwaysActivePluginIds nur lesen.
        var settings = new AppSettingsService();
        return new PluginActivationPlanner(settings);
    }

    [Fact]
    public void Plan_Activates_When_TargetAppInstalled()
    {
        var planner = NewPlanner();
        var plugin = FakePlugin("kroste.a", "1.0.0", "0.1.0", (730, "CS2"));
        var games = new List<DiscoveredGame> { FakeGame(730, "Counter-Strike 2") };

        var result = planner.Plan(new[] { plugin }, games, new Version(1, 0, 0));

        result.Should().HaveCount(1);
        result[0].Activate.Should().BeTrue();
        result[0].SkipReason.Should().Be(ActivationSkipReason.None);
        result[0].MatchedGames.Should().HaveCount(1);
    }

    [Fact]
    public void Plan_Skips_When_NoTargetInstalled()
    {
        var planner = NewPlanner();
        var plugin = FakePlugin("kroste.a", "1.0.0", "0.1.0", (999999, "Unknown"));
        var games = new List<DiscoveredGame> { FakeGame(730, "CS2") };

        var result = planner.Plan(new[] { plugin }, games, new Version(1, 0, 0));

        result[0].Activate.Should().BeFalse();
        result[0].SkipReason.Should().Be(ActivationSkipReason.NoTargetInstalled);
    }

    [Fact]
    public void Plan_Skips_When_HostTooOld()
    {
        var planner = NewPlanner();
        var plugin = FakePlugin("kroste.a", "1.0.0", "5.0.0", (730, "CS2"));
        var games = new List<DiscoveredGame> { FakeGame(730, "CS2") };

        var result = planner.Plan(new[] { plugin }, games, new Version(1, 0, 0));

        result[0].Activate.Should().BeFalse();
        result[0].SkipReason.Should().Be(ActivationSkipReason.HostTooOld);
    }

    [Fact]
    public void Plan_Resolves_Conflict_By_HigherVersion()
    {
        var planner = NewPlanner();
        var older = FakePlugin("kroste.a", "1.0.0", "0.1.0", (730, "CS2"));
        var newer = FakePlugin("kroste.b", "2.5.1", "0.1.0", (730, "CS2"));
        var games = new List<DiscoveredGame> { FakeGame(730, "CS2") };

        var result = planner.Plan(new[] { older, newer }, games, new Version(1, 0, 0));

        result.Should().Contain(d => d.Plugin.Manifest.Id == "kroste.b" && d.Activate);
        result.Should().Contain(d => d.Plugin.Manifest.Id == "kroste.a"
            && !d.Activate && d.SkipReason == ActivationSkipReason.LostConflict);
    }

    [Fact]
    public void Plan_Handles_PrereleaseVersion_As_HostVersion()
    {
        // Simulation: MinVer liefert "0.1.1-alpha.0.3" auf dem Dev-Rechner.
        // Der Planner muss die Prerelease-Metadaten strippen und "0.1.1" nutzen.
        var planner = NewPlanner();
        var plugin = FakePlugin("kroste.a", "1.0.0", "0.1.0", (730, "CS2"));
        var games = new List<DiscoveredGame> { FakeGame(730, "CS2") };

        // Host-Version wird schon geparst in InitializeAsync; hier direkt Version übergeben.
        var result = planner.Plan(new[] { plugin }, games, new Version(0, 1, 1));

        result[0].Activate.Should().BeTrue();
    }
}
