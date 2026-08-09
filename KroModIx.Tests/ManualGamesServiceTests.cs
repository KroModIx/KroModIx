using FluentAssertions;
using KroModIx.Services.Games;
using Xunit;

namespace KroModIx.Tests;

public class ManualGamesServiceTests
{
    [Fact]
    public void Add_Then_Read_Should_Roundtrip()
    {
        var svc = new ManualGamesService();
        var initial = svc.All.Count;
        var entry = svc.Add("Test Game", "/tmp/test-game", "/tmp/test-game/game.exe",
            coverPath: null, steamAppId: 1234567);
        try
        {
            entry.Id.Should().NotBeNullOrEmpty();
            entry.DisplayName.Should().Be("Test Game");
            entry.SteamAppId.Should().Be(1234567);

            var svc2 = new ManualGamesService();
            svc2.All.Should().Contain(g => g.Id == entry.Id);
        }
        finally
        {
            svc.Remove(entry.Id);
        }
    }

    [Fact]
    public void Update_Should_Mutate_And_Persist()
    {
        var svc = new ManualGamesService();
        var entry = svc.Add("Rename Me", "/tmp/rename-me");
        try
        {
            svc.Update(entry.Id, e => e.DisplayName = "Renamed");
            var svc2 = new ManualGamesService();
            svc2.All.Should().Contain(g => g.Id == entry.Id && g.DisplayName == "Renamed");
        }
        finally
        {
            svc.Remove(entry.Id);
        }
    }

    [Fact]
    public void Remove_Should_Delete_Entry()
    {
        var svc = new ManualGamesService();
        var entry = svc.Add("Delete Me", "/tmp/delete-me");

        svc.Remove(entry.Id).Should().BeTrue();

        var svc2 = new ManualGamesService();
        svc2.All.Should().NotContain(g => g.Id == entry.Id);
    }
}
