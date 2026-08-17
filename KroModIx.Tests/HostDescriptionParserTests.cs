using FluentAssertions;
using KroModIx.Services.Text;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Regression-Guard fuer den zentralen Beschreibungs-Parser.
/// Portiert die Schedule-I-BBCode-Fixtures 1:1 — alle Nexus-Plugins nutzen
/// jetzt diesen Parser, also muss das Fixture-Set hier gepflegt werden.</summary>
public class HostDescriptionParserTests
{
    private static readonly HostDescriptionParserImpl P = new();

    // ---- ToPlainText: Container-Tags ----

    [Theory]
    [InlineData("[center]hello[/center]", "hello")]
    [InlineData("[b]bold[/b] text", "bold text")]
    [InlineData("[i]italic[/i]", "italic")]
    [InlineData("[color=#ff0000]red[/color]", "red")]
    [InlineData("[size=1][i][font=Verdana]tiny[/font][/i][/size]", "tiny")]
    [InlineData("[right]right-aligned[/right]", "right-aligned")]
    public void ToPlainText_SimpleContainerTags(string input, string expected)
    {
        P.ToPlainText(input).Should().Be(expected);
    }

    [Fact]
    public void ToPlainText_UrlTag_KeepsTextOnly()
    {
        P.ToPlainText("See [url=https://buymeacoffee.com/foo]buy me a coffee[/url] please")
            .Should().Be("See buy me a coffee please");
    }

    [Fact]
    public void ToPlainText_ImgTag_IsDroppedCompletely()
    {
        P.ToPlainText("before [img height=100]https://media.giphy.com/foo.gif[/img] after")
            .Should().Be("before  after");
    }

    [Fact]
    public void ToPlainText_LineTag_BecomesAsciiDivider()
    {
        var result = P.ToPlainText("top[line]bottom");
        result.Should().Contain("―");
        result.Should().StartWith("top");
        result.Should().EndWith("bottom");
    }

    [Fact]
    public void ToPlainText_RealNexusDescription_NoBracketsLeft()
    {
        var input = @"[center][line]
[/center]
[center][url=https://buymeacoffee.com/foo][img height=100]https://media3.giphy.com/foo.gif[/img][/url][/center]
[right][size=1][i][font=Verdana]Based on the legendary [url=https://www.nexusmods.com/schedule1/mods/907]Drones [/url]mod by [b][color=#00ff00]ThrustGoblin [/color][/b](credit) v0.9.9[/font][/i][/size][/right]";

        var result = P.ToPlainText(input);
        result.Should().NotContain("[");
        result.Should().NotContain("]");
        result.Should().NotContain("giphy.com");
        result.Should().NotContain("buymeacoffee.com");
        result.Should().Contain("ThrustGoblin");
    }

    [Fact]
    public void ToPlainText_EmptyOrNull_ReturnsEmpty()
    {
        P.ToPlainText("").Should().Be("");
        P.ToPlainText(null!).Should().Be("");
    }

    [Fact]
    public void ToPlainText_HtmlEntities_AreDecoded()
    {
        P.ToPlainText("A &amp; B &lt;3").Should().Be("A & B <3");
    }

    // ---- Regression: [*]-Listen-BBCode wurde in v1.20.0 nicht gestrippt
    // (User-Screenshot: „[*]Install MelonLoader for Schedule I. [/*]"). ----

    [Fact]
    public void ToPlainText_ListItem_BecomesBulletLine()
    {
        var input = "[*]Install MelonLoader for Schedule I. [/*]";
        var result = P.ToPlainText(input);
        result.Should().NotContain("[");
        result.Should().NotContain("]");
        result.Should().Contain("Install MelonLoader for Schedule I.");
        result.Should().Contain("•");
    }

    [Fact]
    public void ToPlainText_MultipleListItems_EachBecomesBullet()
    {
        var input = "[*]First[/*] [*]Second[/*] [*]Third[/*]";
        var result = P.ToPlainText(input);
        result.Should().NotContain("[");
        result.Should().NotContain("]");
        // 3 Bullets
        var bulletCount = 0;
        foreach (var ch in result) if (ch == '•') bulletCount++;
        bulletCount.Should().Be(3);
    }

    [Fact]
    public void ToPlainText_UnknownStandaloneTag_IsStripped()
    {
        // z.B. [hr], [*], [some_unknown_thing]
        P.ToPlainText("before [hr] after").Should().Be("before  after");
        P.ToPlainText("x [some_unknown] y").Should().Be("x  y");
    }

    // ---- ExtractImages ----

    [Fact]
    public void ExtractImages_BbCodeImg_ReturnsUrlAndHeight()
    {
        var input = "[img height=200]https://foo.png[/img]";
        var imgs = P.ExtractImages(input);
        imgs.Should().HaveCount(1);
        imgs[0].Url.Should().Be("https://foo.png");
        imgs[0].Height.Should().Be(200);
    }

    [Fact]
    public void ExtractImages_HtmlImg_ReturnsUrl()
    {
        var input = "<img src=\"https://a.png\" alt=\"x\"> and <img src='https://b.jpg'>";
        var imgs = P.ExtractImages(input);
        imgs.Should().HaveCount(2);
        imgs[0].Url.Should().Be("https://a.png");
        imgs[1].Url.Should().Be("https://b.jpg");
    }

    [Fact]
    public void ExtractImages_MixedBbCodeAndHtml_ReturnsAllInOrder()
    {
        var input = "[img]https://one.png[/img] then <img src=\"https://two.png\"> and [img height=50]https://three.png[/img]";
        var imgs = P.ExtractImages(input);
        imgs.Should().HaveCount(3);
        imgs[0].Url.Should().Be("https://one.png");
        imgs[1].Url.Should().Be("https://two.png");
        imgs[2].Url.Should().Be("https://three.png");
        imgs[2].Height.Should().Be(50);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text without any tags")]
    [InlineData("[url=https://foo]link[/url]")]
    public void ExtractImages_NoImages_ReturnsEmpty(string input)
    {
        P.ExtractImages(input).Should().BeEmpty();
    }

    // ---- ToHtml (v1.21) ----

    [Theory]
    [InlineData("[b]bold[/b]", "<strong>bold</strong>")]
    [InlineData("[i]it[/i]", "<em>it</em>")]
    [InlineData("[u]ul[/u]", "<u>ul</u>")]
    [InlineData("[center]x[/center]", "<div class=\"center\">x</div>")]
    [InlineData("[right]x[/right]", "<div class=\"right\">x</div>")]
    public void ToHtml_ContainerTags_MapToHtml(string input, string expected)
    {
        P.ToHtml(input).Should().Be(expected);
    }

    [Fact]
    public void ToHtml_Url_MapsToAnchor()
    {
        P.ToHtml("[url=https://foo.example]click[/url]")
            .Should().Be("<a href=\"https://foo.example\">click</a>");
    }

    [Fact]
    public void ToHtml_Img_MapsToImgTag()
    {
        var html = P.ToHtml("[img height=200]https://media.example/a.png[/img]");
        html.Should().Contain("<img src=\"https://media.example/a.png\"");
        html.Should().Contain("max-height:200px");
    }

    [Fact]
    public void ToHtml_Color_MapsToInlineStyle()
    {
        P.ToHtml("[color=#ff0000]red[/color]")
            .Should().Contain("<span style=\"color:#ff0000\">red</span>");
    }

    [Fact]
    public void ToHtml_List_MapsToUlLi()
    {
        var html = P.ToHtml("[list][*]First[*]Second[/list]");
        html.Should().Contain("<ul>");
        html.Should().Contain("<li>First</li>");
        html.Should().Contain("<li>Second</li>");
        html.Should().Contain("</ul>");
    }

    [Fact]
    public void ToHtml_HtmlPassesThrough()
    {
        // Reine HTML-Inputs (viele Nexus-Descriptions kommen direkt in HTML)
        // bleiben unveraendert erhalten. [b]<br> im gemischten Input wird
        // trotzdem korrekt konvertiert.
        P.ToHtml("<p>hi</p>").Should().Contain("<p>hi</p>");
    }

    [Fact]
    public void ToHtml_EmptyOrNull_ReturnsEmpty()
    {
        P.ToHtml("").Should().Be("");
        P.ToHtml(null!).Should().Be("");
    }
}
