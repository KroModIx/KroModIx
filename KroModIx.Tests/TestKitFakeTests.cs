using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.TestKit;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Das TestKit wird als eigenes Paket an die Plugin-Repos
/// ausgeliefert. Es gehört damit geprüft wie Auslieferungs-Code — eine
/// Attrappe, die stillschweigend anders antwortet als der Host, macht acht
/// Plugin-Suiten grün und das Spiel kaputt.</summary>
public class TestKitFakeTests
{
    /// <summary>Der Zweig, für den es sonst keinen Weg gäbe: greift die
    /// Raten-Sperre, kennt der echte Dienst nur den Tag und keine
    /// Dateiliste. Im Alltag läuft das fast nie — also genau der Zweig, der
    /// unbemerkt kaputtgeht.</summary>
    [Fact]
    public async Task FakeGitHubGibtBeiRatenSperreNurDenTag()
    {
        var gh = new FakeGitHubService()
            .AddRelease("UE4SS-RE/RE-UE4SS", "v3.0.1", "UE4SS_v3.0.1.zip");

        gh.RateLimited = true;

        var latest = await gh.GetLatestReleaseAsync("UE4SS-RE/RE-UE4SS", TestContext.Current.CancellationToken);
        latest.Should().NotBeNull();
        latest!.Tag.Should().Be("v3.0.1");
        latest.Assets.Should().BeEmpty();
        latest.FromRedirectChase.Should().BeTrue();

        (await gh.FindLatestAssetAsync("UE4SS-RE/RE-UE4SS", n => n.EndsWith(".zip"), TestContext.Current.CancellationToken))
            .Should().BeNull("ohne Dateiliste gibt es keine Datei zu finden");
        (await gh.GetReleasesAsync("UE4SS-RE/RE-UE4SS", ct: TestContext.Current.CancellationToken)).Should().BeEmpty(
            "die Ausgabenliste geht nur über die API, nie über den Umleitungs-Pfad");

        // Der Ausweg, den ein Plugin dann nehmen muss.
        gh.BuildAssetUrl("UE4SS-RE/RE-UE4SS", latest.Tag, "UE4SS_v3.0.1.zip")
            .Should().Be("https://github.com/UE4SS-RE/RE-UE4SS/releases/download/v3.0.1/UE4SS_v3.0.1.zip");
    }

    [Fact]
    public async Task FakeGitHubFindetDieErsteTreffendeDateiUndZaehltAbfragen()
    {
        var gh = new FakeGitHubService()
            .AddRelease("Kroste/Loader", "v2.0.0", "Loader-win-x64.zip", "Loader-linux.tar.gz")
            .AddRelease("Kroste/Loader", "v1.9.0", "Loader-win-x64.zip");

        var hit = await gh.FindLatestAssetAsync("Kroste/Loader", n => n.Contains("win"), TestContext.Current.CancellationToken);
        hit.Should().NotBeNull();
        hit!.Value.Release.Tag.Should().Be("v2.0.0", "die zuerst hinterlegte gilt als die neueste");
        hit.Value.Asset.Name.Should().Be("Loader-win-x64.zip");

        (await gh.FindLatestAssetAsync("Kroste/Loader", n => n.EndsWith(".exe"), TestContext.Current.CancellationToken))
            .Should().BeNull();
        (await gh.GetLatestReleaseAsync("Gibts/Nicht", TestContext.Current.CancellationToken)).Should().BeNull();

        gh.Queries.Should().HaveCount(3, "jede Abfrage wird mitgeschrieben, damit ein Test "
            + "prüfen kann, dass ein Plugin einmal fragt und nicht bei jedem Aufbau der Oberfläche");
    }

    /// <summary>Die Attrappe packt echte ZIPs aus — ein Plugin-Test prüft
    /// damit, wo seine Dateien landen, nicht nur dass eine Methode gerufen
    /// wurde. Zugleich zeigt sie den Vorgang mit seinen Optionen an.</summary>
    [Fact]
    public void FakeArchiveServicePacktAusUndProtokolliertDenAufruf()
    {
        var tmp = Directory.CreateTempSubdirectory("testkit-arch").FullName;
        var zip = Path.Combine(tmp, "mod.zip");
        using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            a.CreateEntry("MeineMod/Scripts/main.lua");
            a.CreateEntry("MeineMod/liesmich.txt");
            a.CreateEntry("/tmp/ausserhalb.txt");
        }

        var ziel = Path.Combine(tmp, "ziel");
        var svc = new FakeArchiveService();
        var r = svc.Extract(zip, ziel, new ArchiveExtractOptions(
            StripPrefix: "MeineMod", Filter: p => p.EndsWith(".lua")));

        r.ExtractedPaths.Should().ContainSingle()
            .Which.Should().Be(Path.Combine(ziel, "Scripts", "main.lua"));
        r.SkippedUnsafe.Should().BeEmpty("der absolute Eintrag fiel schon durch den Filter");

        svc.ExtractCalls.Should().ContainSingle();
        svc.ExtractCalls[0].Options.StripPrefix.Should().Be("MeineMod");

        // Ohne Filter muss der Ausbruchsversuch gemeldet werden, nicht still
        // uebersprungen: ein Aufrufer soll das berichten koennen.
        var r2 = svc.Extract(zip, Path.Combine(tmp, "ziel2"));
        r2.SkippedUnsafe.Should().ContainSingle().Which.Should().Be("/tmp/ausserhalb.txt");
        File.Exists("/tmp/ausserhalb.txt").Should().BeFalse();

        svc.DetectKind(zip).Should().Be(ArchiveKind.Zip);
        svc.List(zip).Select(e => e.Path).Should().Contain("MeineMod/liesmich.txt");

        Directory.Delete(tmp, true);
    }

    [Fact]
    public void FakeUnrealPakServiceSchreibtUndLiestWieder()
    {
        var tmp = Directory.CreateTempSubdirectory("testkit-pak").FullName;
        var pak = Path.Combine(tmp, "mod_P.pak");

        var svc = new FakeUnrealPakService();
        svc.IsPakFile(pak).Should().BeFalse("noch nichts geschrieben");

        var b = svc.CreateBuilder("../../../Icarus/Content/");
        b.Add("data/D_ItemsStatic.json", System.Text.Encoding.UTF8.GetBytes("{\"Rows\":[]}"));
        b.Count.Should().Be(1);
        b.Write(pak);

        svc.IsPakFile(pak).Should().BeTrue();
        using var r = svc.OpenRead(pak);
        r.MountPoint.Should().Be("../../../Icarus/Content/");
        r.Contains("data/D_ItemsStatic.json").Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(r.Read("data/D_ItemsStatic.json"))
            .Should().Be("{\"Rows\":[]}");
        r.IndexHash.Should().NotBeEmpty("die Staleness-Prüfung der Plugins hängt daran");

        Directory.Delete(tmp, true);
    }
}
