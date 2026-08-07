using FluentAssertions;
using ModManager.PluginContracts;
using Xunit;

namespace ModManager.Tests;

public class PluginManifestTests
{
    [Fact]
    public void FromJson_Should_ParseFullManifest()
    {
        var json = """
        {
          "schema": 1,
          "id": "kroste.dummy",
          "displayName": "Dummy Plugin",
          "version": "0.1.0",
          "assembly": "ModManager.Plugins.Dummy.dll",
          "entryType": "ModManager.Plugins.Dummy.DummyPlugin",
          "minHostVersion": "0.2.0",
          "author": "Kroste",
          "description": "test",
          "targets": [
            {
              "gameId": "counter-strike-2",
              "displayName": "Counter-Strike 2",
              "steamAppId": 730,
              "alternativeExecutableNames": [],
              "platforms": "Both"
            }
          ],
          "updateSource": { "kind": "github", "repo": "Kroste/Test" }
        }
        """;

        var m = PluginManifest.FromJson(json);

        m.Id.Should().Be("kroste.dummy");
        m.Assembly.Should().Be("ModManager.Plugins.Dummy.dll");
        m.EntryType.Should().Be("ModManager.Plugins.Dummy.DummyPlugin");
        m.Targets.Should().HaveCount(1);
        m.Targets[0].GameId.Should().Be("counter-strike-2");
        m.Targets[0].SteamAppId.Should().Be(730);
        m.Targets[0].Platforms.Should().Be(Platforms.Both);
        m.UpdateSource!.Kind.Should().Be("github");
        m.UpdateSource.Repo.Should().Be("Kroste/Test");
    }
}
