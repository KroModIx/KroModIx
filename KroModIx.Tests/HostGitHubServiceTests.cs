using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.GitHub;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Antwortet mit vorgegebenen Antworten und zählt mit, welcher Weg
/// genommen wurde.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(respond(request));
    }
}

/// <summary>Der GitHub-Baukasten ist die Stelle, an der sieben vorher
/// getrennte Kopien zusammenlaufen. Geprüft werden die Teile, die dort
/// schiefgehen können: welcher Weg genommen wird, was die Raten-Sperre
/// bewirkt, und ob der Baukasten ehrlich meldet, wenn er keine Dateiliste
/// hat.</summary>
public sealed class HostGitHubServiceTests
{
    private const string ApiLatest = "https://api.github.com/repos/Besitzer/Repo/releases/latest";
    private const string PageLatest = "https://github.com/Besitzer/Repo/releases/latest";

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage RateLimited()
        => new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"message\":\"API rate limit exceeded\"}"),
        };

    private static HttpResponseMessage Redirect(string tag)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri($"https://github.com/Besitzer/Repo/releases/tag/{tag}");
        return r;
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v3.0.1",
          "html_url": "https://github.com/Besitzer/Repo/releases/tag/v3.0.1",
          "prerelease": false,
          "draft": false,
          "assets": [
            { "name": "zDEV-UE4SS_v3.0.1.zip", "browser_download_url": "https://cdn/zdev.zip", "size": 24000000 },
            { "name": "UE4SS_v3.0.1.zip", "browser_download_url": "https://cdn/ue4ss.zip", "size": 5500000 },
            { "name": "zMapGenBP.zip", "browser_download_url": "https://cdn/map.zip", "size": 28000 }
          ]
        }
        """;

    [Fact]
    public async Task ApiWegLiefertTagUndDateien()
    {
        var handler = new FakeHttpHandler(_ => Json(ReleaseJson));
        var svc = new HostGitHubServiceImpl(_ => handler);

        var release = await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);

        release.Should().NotBeNull();
        release!.Tag.Should().Be("v3.0.1");
        release.Version.Should().Be("3.0.1", "das führende v gehört nicht in den Versionsvergleich");
        release.FromRedirectChase.Should().BeFalse();
        release.Assets.Should().HaveCount(3);
        handler.Requests.Should().ContainSingle().Which.Should().Be(ApiLatest);
    }

    /// <summary>Der Grund für das Prädikat: ein UE4SS-Release hat vier ZIPs,
    /// und das 24 MB große Entwickler-Archiv steht in der Liste vor dem
    /// richtigen. Wer „erstes ZIP" nimmt, lädt das Falsche.</summary>
    [Fact]
    public async Task FindLatestAssetWaehltNachPraedikatUndNichtNachReihenfolge()
    {
        var svc = new HostGitHubServiceImpl(_ => new FakeHttpHandler(_ => Json(ReleaseJson)));

        var found = await svc.FindLatestAssetAsync("Besitzer/Repo",
            name => name.StartsWith("UE4SS_v", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("zDEV", StringComparison.OrdinalIgnoreCase),
            TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.Value.Asset.Name.Should().Be("UE4SS_v3.0.1.zip");
        found.Value.Asset.DownloadUrl.Should().Be("https://cdn/ue4ss.zip");
        found.Value.Release.Version.Should().Be("3.0.1");
    }

    [Fact]
    public async Task BeiRatenSperreKommtDerUmleitungsPfad()
    {
        var handler = new FakeHttpHandler(req =>
            req.RequestUri!.Host == "api.github.com" ? RateLimited() : Redirect("v2.5.2"));
        var svc = new HostGitHubServiceImpl(_ => handler);

        var release = await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);

        release.Should().NotBeNull();
        release!.Tag.Should().Be("v2.5.2");
        release.Version.Should().Be("2.5.2");
        release.FromRedirectChase.Should().BeTrue(
            "ohne API gibt es keine Dateiliste — das muss das Ergebnis sagen");
        release.Assets.Should().BeEmpty();
        svc.IsRateLimited.Should().BeTrue();
        handler.Requests.Should().BeEquivalentTo([ApiLatest, PageLatest]);
    }

    /// <summary>Der eigentliche Gewinn der Zusammenlegung: nach der ersten
    /// Sperre fragt kein weiterer Aufrufer die API noch an. Vorher entdeckte
    /// jeder der sieben Verbraucher das Limit für sich und verbrannte dabei
    /// eine Anfrage.</summary>
    [Fact]
    public async Task NachDerSperreGehtKeineWeitereAnfrageAnDieApi()
    {
        var handler = new FakeHttpHandler(req =>
            req.RequestUri!.Host == "api.github.com" ? RateLimited() : Redirect("v1.0.0"));
        var svc = new HostGitHubServiceImpl(_ => handler);

        await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);
        await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);
        await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);

        handler.Requests.Count(u => u.StartsWith("https://api.github.com")).Should().Be(1,
            "nur der erste Versuch darf ins Limit laufen");
        handler.Requests.Count(u => u == PageLatest).Should().Be(3);
    }

    /// <summary>Der verbindliche Weg: GitHub schickt bei erreichtem Limit
    /// einen 403 <b>mit</b> <c>x-ratelimit-remaining: 0</c>. Das ist die
    /// Prüfung, die trägt — die Meldung der Ausnahme enthält den Antworttext
    /// nämlich nie (nachgemessen: „Response status code does not indicate
    /// success: 403 (Forbidden)." und nichts weiter). Genau deshalb war die
    /// vorherige Prüfung über <c>ex.Message.Contains("rate limit")</c> an
    /// drei Host-Stellen toter Code.</summary>
    [Fact]
    public async Task DerRatenKopfIstDerVerbindlicheWeg()
    {
        var handler = new FakeHttpHandler(req =>
        {
            if (req.RequestUri!.Host != "api.github.com") return Redirect("v1.2.3");
            // 403 OHNE Hinweis im Text, aber MIT dem Kopf.
            var r = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"Forbidden\"}"),
            };
            r.Headers.Add("x-ratelimit-remaining", "0");
            return r;
        });
        var svc = new HostGitHubServiceImpl(_ => handler);

        var release = await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);

        release!.Tag.Should().Be("v1.2.3");
        svc.IsRateLimited.Should().BeTrue();
    }

    /// <summary>Ein 403 mit noch offenem Kontingent ist etwas anderes —
    /// fehlende Rechte, privates Repo. Die Sperre darf davon nicht greifen,
    /// sonst blockiert ein einzelnes Repo eine Stunde lang alle
    /// anderen.</summary>
    [Fact]
    public async Task EinVerbotenOhneErschoepftesKontingentSperrtNicht()
    {
        var handler = new FakeHttpHandler(req =>
        {
            if (req.RequestUri!.Host != "api.github.com") return Redirect("v1.2.3");
            var r = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"Must have admin rights\"}"),
            };
            r.Headers.Add("x-ratelimit-remaining", "58");
            return r;
        });
        var svc = new HostGitHubServiceImpl(_ => handler);

        await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);
        svc.IsRateLimited.Should().BeFalse();
    }

    /// <summary>429 ist das sekundäre Limit — muss dieselbe Sperre
    /// auslösen.</summary>
    [Fact]
    public async Task ZuVieleAnfragenLoestDieSperreEbenfallsAus()
    {
        var handler = new FakeHttpHandler(req =>
            req.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : Redirect("v1.0.0"));
        var svc = new HostGitHubServiceImpl(_ => handler);

        await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);
        svc.IsRateLimited.Should().BeTrue();
    }

    /// <summary>Ein 404 ist kein Limit — die Sperre darf davon nicht
    /// ausgelöst werden, sonst blockiert ein einzelnes Repo ohne Releases
    /// eine Stunde lang alle anderen.</summary>
    [Fact]
    public async Task EinFehlendesReleaseLoestKeineSperreAus()
    {
        var handler = new FakeHttpHandler(req =>
            req.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Redirect("v9.9.9"));
        var svc = new HostGitHubServiceImpl(_ => handler);

        var release = await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken);

        release!.Tag.Should().Be("v9.9.9", "der Umleitungs-Pfad greift trotzdem");
        svc.IsRateLimited.Should().BeFalse();
    }

    [Fact]
    public async Task OhneTagImLocationKopfKommtNull()
    {
        var handler = new FakeHttpHandler(req =>
            req.RequestUri!.Host == "api.github.com"
                ? RateLimited()
                : new HttpResponseMessage(HttpStatusCode.OK));
        var svc = new HostGitHubServiceImpl(_ => handler);

        (await svc.GetLatestReleaseAsync("Besitzer/Repo", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    /// <summary>Wenn die gesuchte Datei nicht an der neuesten Ausgabe hängt,
    /// wird in den älteren weitergesucht. Real bei Loadern, die eine Ausgabe
    /// ohne das gewünschte Archiv veröffentlichen.</summary>
    [Fact]
    public async Task SuchtInAelterenAusgabenWeiter()
    {
        const string ohneAsset = """
            { "tag_name": "v4.0.0", "html_url": "u", "prerelease": false, "draft": false, "assets": [] }
            """;
        const string liste = """
            [
              { "tag_name": "v4.0.0", "html_url": "u", "prerelease": false, "draft": false, "assets": [] },
              { "tag_name": "v3.9.0", "html_url": "u", "prerelease": true,  "draft": false,
                "assets": [ { "name": "Loader.zip", "browser_download_url": "https://cdn/pre.zip", "size": 1 } ] },
              { "tag_name": "v3.8.0", "html_url": "u", "prerelease": false, "draft": false,
                "assets": [ { "name": "Loader.zip", "browser_download_url": "https://cdn/alt.zip", "size": 1 } ] }
            ]
            """;
        var svc = new HostGitHubServiceImpl(_ => new FakeHttpHandler(req =>
            Json(req.RequestUri!.ToString().Contains("per_page") ? liste : ohneAsset)));

        var found = await svc.FindLatestAssetAsync("Besitzer/Repo", n => n == "Loader.zip", TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.Value.Release.Tag.Should().Be("v3.8.0",
            "die Vorab-Ausgabe v3.9.0 wird übersprungen");
        found.Value.Asset.DownloadUrl.Should().Be("https://cdn/alt.zip");
    }

    [Fact]
    public async Task VorabAusgabenBleibenStandardmaessigAussen()
    {
        const string liste = """
            [
              { "tag_name": "v5.0.0-rc1", "html_url": "u", "prerelease": true, "draft": false, "assets": [] },
              { "tag_name": "v4.0.0", "html_url": "u", "prerelease": false, "draft": false, "assets": [] }
            ]
            """;
        var svc = new HostGitHubServiceImpl(_ => new FakeHttpHandler(_ => Json(liste)));

        (await svc.GetReleasesAsync("Besitzer/Repo", ct: TestContext.Current.CancellationToken)).Select(r => r.Tag)
            .Should().BeEquivalentTo(["v4.0.0"]);
        (await svc.GetReleasesAsync("Besitzer/Repo", includePrerelease: true, ct: TestContext.Current.CancellationToken)).Select(r => r.Tag)
            .Should().BeEquivalentTo(["v5.0.0-rc1", "v4.0.0"]);
    }

    [Fact]
    public async Task LeeresRepoWirdAbgelehntOhneAnfrage()
    {
        var handler = new FakeHttpHandler(_ => Json(ReleaseJson));
        var svc = new HostGitHubServiceImpl(_ => handler);

        (await svc.GetLatestReleaseAsync("", TestContext.Current.CancellationToken)).Should().BeNull();
        (await svc.GetReleasesAsync("  ", ct: TestContext.Current.CancellationToken)).Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("v3.0.1", "3.0.1")]
    [InlineData("V3.0.1", "3.0.1")]
    [InlineData("3.0.1", "3.0.1")]
    [InlineData("week252", "week252")]
    public void VersionOhneFuehrendesV(string tag, string expected)
        => HostGitHubServiceImpl.StripV(tag).Should().Be(expected);

    [Fact]
    public void AssetUrlFolgtDerGitHubKonvention()
        => new HostGitHubServiceImpl()
            .BuildAssetUrl("KroModIx/KroModIx", "v1.31.0", "KroModIx-1.31.0-x86_64.AppImage")
            .Should().Be("https://github.com/KroModIx/KroModIx/releases/download/v1.31.0/" +
                         "KroModIx-1.31.0-x86_64.AppImage");

    [Fact]
    public async Task NullImplementierungAntwortetLeerOhneZuWerfen()
    {
        var svc = NullGitHubService.Instance;
        svc.IsRateLimited.Should().BeFalse();
        (await svc.GetLatestReleaseAsync("a/b", TestContext.Current.CancellationToken)).Should().BeNull();
        (await svc.GetReleasesAsync("a/b", ct: TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await svc.FindLatestAssetAsync("a/b", _ => true, TestContext.Current.CancellationToken)).Should().BeNull();
        // Die URL-Bildung ist reine Zeichenkettenarbeit und funktioniert auch
        // ohne Host — kein Grund, sie zu verweigern.
        svc.BuildAssetUrl("a/b", "v1", "x.zip").Should()
            .Be("https://github.com/a/b/releases/download/v1/x.zip");
    }
}
