using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Services.Text;

/// <summary>Host-Impl des <see cref="IDescriptionParser"/>. Portiert aus
/// Schedule I v0.2.0 (dort war es noch als plugin-lokaler
/// <c>NexusDescriptionParser</c>). Alle Kroste-Plugins nutzen ab
/// Contracts v1.19 diesen zentralen Parser statt eigener Regex-Chains.
///
/// <para><b>Nexus-Descriptions mixen HTML und BBCode</b> — reine HTML-
/// Stripper lassen BBCode roh stehen (real passiert im Schedule-I-v0.1:
/// <c>[center][url=..][img height=100]https://…[/img][/url][/center]</c>).
/// Dieser Parser strippt beide Formate sauber.</para></summary>
public sealed class HostDescriptionParserImpl : IDescriptionParser
{
    // ---- Regex-Konstanten (Compiled fuer Multi-Plugin-Nutzung) ----

    private static readonly Regex HtmlBr = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlPClose = new(@"</p\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlAnyTag = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex BbImg = new(
        @"\[img[^\]]*\][^\[]*\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BbUrlWithArg = new(
        @"\[url=[^\]]*\](.*?)\[/url\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbUrlBare = new(
        @"\[url\](.*?)\[/url\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbLine = new(@"\[line\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BbBr = new(@"\[br\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BbStandaloneTag = new(@"\[/?[a-zA-Z][^\]]*\]", RegexOptions.Compiled);

    private static readonly Regex TrailingSpacesBeforeNewline = new(@"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex MultiBlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>Container-Tags mit optional <c>=argument</c>. Inhalt bleibt,
    /// Tag verschwindet.</summary>
    private static readonly string[] ContainerTags =
    {
        "center", "right", "left", "b", "i", "u", "s",
        "size", "color", "font", "quote", "spoiler", "code", "sub", "sup",
        "list", "credit", "youtube",
    };

    // ---- Image-Extraction ----

    private static readonly Regex BbImgWithUrl = new(
        @"\[img(?<attrs>[^\]]*)\](?<url>[^\[]*)\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlImg = new(
        @"<img[^>]*\bsrc\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeightAttr = new(
        @"\bheight\s*=\s*[""']?(?<h>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ---- Public API ----

    public string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = html;

        // HTML zuerst.
        s = HtmlBr.Replace(s, "\n");
        s = HtmlPClose.Replace(s, "\n\n");
        s = HtmlAnyTag.Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s);

        // BBCode. Wichtig: [img] VOR [url] laufen — sonst wird die Inner-URL
        // doppelt gefressen wenn ein Author [url=..][img]..[/img][/url] baut.
        s = BbImg.Replace(s, "");
        s = BbUrlWithArg.Replace(s, "$1");
        s = BbUrlBare.Replace(s, "$1");
        s = BbLine.Replace(s, "\n― ― ― ― ― ― ― ― ― ―\n");
        s = BbBr.Replace(s, "\n");

        foreach (var tag in ContainerTags)
        {
            s = Regex.Replace(s, $@"\[{tag}(?:=[^\]]*)?\](.*?)\[/{tag}\]", "$1",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        // Restliche Standalone-Tags ([*] in Listen, [hr], etc.) weg.
        s = BbStandaloneTag.Replace(s, "");

        // Whitespace-Cleanup.
        s = TrailingSpacesBeforeNewline.Replace(s, "\n");
        s = MultiBlankLines.Replace(s, "\n\n");
        return s.Trim();
    }

    public IReadOnlyList<InlineImage> ExtractImages(string html)
    {
        if (string.IsNullOrEmpty(html)) return Array.Empty<InlineImage>();

        // Alle Matches sammeln + nach Position im Dokument sortieren, damit
        // die Rueckgabe die reale Reihenfolge widerspiegelt (BBCode + HTML
        // koennen gemischt vorkommen).
        var buckets = new List<(int Pos, InlineImage Img)>();

        foreach (Match m in BbImgWithUrl.Matches(html))
        {
            var url = m.Groups["url"].Value.Trim();
            if (url.Length == 0) continue;
            int? h = null;
            var hm = HeightAttr.Match(m.Groups["attrs"].Value);
            if (hm.Success && int.TryParse(hm.Groups["h"].Value, out var parsed)) h = parsed;
            buckets.Add((m.Index, new InlineImage(url, h)));
        }

        // HTML <img src="URL" …> — kein Height-Parse (wenig zuverlaessig
        // wegen CSS-Overrides, wir lassen es lieber offen als falsche
        // Werte zu liefern).
        foreach (Match m in HtmlImg.Matches(html))
        {
            var url = m.Groups["url"].Value.Trim();
            if (url.Length == 0) continue;
            buckets.Add((m.Index, new InlineImage(url)));
        }

        buckets.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        var result = new List<InlineImage>(buckets.Count);
        foreach (var (_, img) in buckets) result.Add(img);
        return result;
    }
}
