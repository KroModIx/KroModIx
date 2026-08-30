using KroModIx.Plugin.Contracts;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Contracts-Baukasten fuer den Versions-Vergleich (v1.26).
/// Die Faelle stammen aus den Bugs, die die Plugin-eigenen Varianten
/// hatten — lokale Version neuer als Katalog, 0.9 vs 0.10, Suffixe.</summary>
public sealed class VersionCompareTests
{
    [Theory]
    [InlineData("0.4.5", "0.5")]     // Katalog aelter als installiert
    [InlineData("0.5", "0.5")]
    [InlineData("v0.5", "0.5")]
    [InlineData("1.0", "1.0.0")]
    [InlineData("0.5", "0.5-pc")]
    [InlineData("[v0.5]", "0.5")]
    [InlineData("0.9", "0.10")]
    [InlineData("1.0", "1.0a")]
    [InlineData("1.0 beta", "1.0")]
    [InlineData("Ep. 6", "Ep. 5")]   // nicht parsebar → kein Update
    [InlineData(null, "0.5")]
    [InlineData("0.5", null)]
    public void KeinUpdate(string? candidate, string? installed)
        => Assert.False(VersionCompare.IsNewer(candidate, installed));

    [Theory]
    [InlineData("0.5", "0.4.5")]
    [InlineData("0.10", "0.9")]
    [InlineData("1.0.1", "1.0")]
    [InlineData("v0.6", "0.5")]
    [InlineData("0.4.5b", "0.4.5a")]
    [InlineData("0.4.5a", "0.4.5")]
    [InlineData("1.0", "1.0 beta")]
    [InlineData("3", "2")]           // Version.TryParse haette hier aufgegeben
    [InlineData("1.2.3-hotfix", "1.2.3")]
    public void Update(string candidate, string installed)
        => Assert.True(VersionCompare.IsNewer(candidate, installed));

    [Fact]
    public void Compare_meldet_Unvergleichbarkeit_statt_zu_raten()
    {
        Assert.Null(VersionCompare.Compare("Ep. 5", "0.5"));
        Assert.NotNull(VersionCompare.Compare("0.5", "0.4"));
    }

    [Fact]
    public void Comparer_sortiert_numerisch()
    {
        string[] versions = ["0.9", "0.10", "0.4.5", "1.0"];
        System.Array.Sort(versions, VersionCompare.Comparer!);
        Assert.Equal(["0.4.5", "0.9", "0.10", "1.0"], versions);
    }
}
