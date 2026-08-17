using FluentAssertions;
using KroModIx.Services.Steam;
using Xunit;

namespace KroModIx.Tests;

public class SteamGameFilterTests
{
    [Theory]
    [InlineData(1493710, "Proton Experimental")]
    [InlineData(1628350, "Steam Linux Runtime 3.0 (sniper)")]
    [InlineData(228980,  "Steamworks Common Redistributables")]
    [InlineData(9999999, "Proton 42.0")] // unbekannte AppId, aber Name greift
    [InlineData(9999998, "Steam Linux Runtime 5.0 (newname)")]
    public void IsTool_Should_MatchKnownTools(int appId, string name)
    {
        SteamGameFilter.IsTool(appId, name).Should().BeTrue();
    }

    [Theory]
    [InlineData(730,     "Counter-Strike 2")]
    [InlineData(2300320, "Farming Simulator 25")]
    [InlineData(526870,  "Satisfactory")]
    [InlineData(526871,  "SomeProtonThing")] // "Proton" nicht als Prefix (kein Leerzeichen)
    [InlineData(3164500, "Schedule I")] // Regression: v1.18 hatte 3164500 faelschlich als "Proton 10.0 (vermutlich)"
    [InlineData(1366540, "Dyson Sphere Program")]
    [InlineData(1091500, "Cyberpunk 2077")]
    public void IsTool_Should_NotFlag_RealGames(int appId, string name)
    {
        SteamGameFilter.IsTool(appId, name).Should().BeFalse();
    }
}
