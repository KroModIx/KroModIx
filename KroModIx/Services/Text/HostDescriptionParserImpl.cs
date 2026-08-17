using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KroModIx.Plugin.Contracts;
using NLog;
using TheArtOfDev.HtmlRenderer.Avalonia;

namespace KroModIx.Services.Text;

/// <summary>Host-Impl des <see cref="IDescriptionParser"/>. Portiert aus
/// Schedule I v0.2.0 (dort war es noch als plugin-lokaler
/// <c>NexusDescriptionParser</c>). Alle Kroste-Plugins nutzen ab
/// Contracts v1.19 diesen zentralen Parser statt eigener Regex-Chains.
///
/// <para><b>v1.21:</b> zusaetzlich <see cref="ToHtml(string)"/> +
/// <see cref="CreateRichView(string)"/> — statt Plain-Text-TextBlock
/// wird ein <see cref="HtmlPanel"/> mit gestyltem Kroste-CSS zurueckgegeben
/// (klickbare Links, Inline-Bilder, Bold/Italic).</para>
///
/// <para><b>Nexus-Descriptions mixen HTML und BBCode</b> — reine HTML-
/// Stripper lassen BBCode roh stehen (real passiert im Schedule-I-v0.1:
/// <c>[center][url=..][img height=100]https://…[/img][/url][/center]</c>).
/// Dieser Parser strippt beide Formate sauber.</para></summary>
public sealed class HostDescriptionParserImpl : IDescriptionParser
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // ==== ToPlainText-Regex ====

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
    private static readonly Regex BbStandaloneTag = new(@"\[/?[^\[\]\n]{1,32}\]", RegexOptions.Compiled);
    private static readonly Regex BbListItem = new(@"\[\*\]\s*", RegexOptions.Compiled);
    private static readonly Regex BbListItemClose = new(@"\[/\*\]", RegexOptions.Compiled);

    private static readonly Regex TrailingSpacesBeforeNewline = new(@"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex MultiBlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    private static readonly string[] ContainerTags =
    {
        "center", "right", "left", "b", "i", "u", "s",
        "size", "color", "font", "quote", "spoiler", "code", "sub", "sup",
        "list", "credit", "youtube",
    };

    // ==== ExtractImages-Regex ====

    private static readonly Regex BbImgWithUrl = new(
        @"\[img(?<attrs>[^\]]*)\](?<url>[^\[]*)\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlImg = new(
        @"<img[^>]*\bsrc\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeightAttr = new(
        @"\bheight\s*=\s*[""']?(?<h>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ==== BBCode→HTML-Regex ====

    private static readonly Regex BbCenter = MakeContainer("center");
    private static readonly Regex BbRight = MakeContainer("right");
    private static readonly Regex BbLeft = MakeContainer("left");
    private static readonly Regex BbBold = MakeContainer("b");
    private static readonly Regex BbItalic = MakeContainer("i");
    private static readonly Regex BbUnderline = MakeContainer("u");
    private static readonly Regex BbStrike = MakeContainer("s");
    private static readonly Regex BbSub = MakeContainer("sub");
    private static readonly Regex BbSup = MakeContainer("sup");
    private static readonly Regex BbQuote = MakeContainer("quote");
    private static readonly Regex BbSpoiler = MakeContainer("spoiler");
    private static readonly Regex BbCredit = MakeContainer("credit");
    private static readonly Regex BbCodeTag = MakeContainer("code");
    // Tags mit Argument die wir zu span/div mit style umsetzen wollen:
    private static readonly Regex BbColor = new(@"\[color=(?<v>[^\]]+)\](?<c>.*?)\[/color\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbSize = new(@"\[size=(?<v>[^\]]+)\](?<c>.*?)\[/size\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbFont = new(@"\[font=(?<v>[^\]]+)\](?<c>.*?)\[/font\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbListBlock = new(@"\[list\](.*?)\[/list\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbYoutube = new(@"\[youtube\](?<id>[^\[]+)\[/youtube\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BbImgWithHtml = new(
        @"\[img(?<attrs>[^\]]*)\](?<url>[^\[]*)\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BbUrlWithArgHtml = new(
        @"\[url=(?<u>[^\]]+)\](?<t>.*?)\[/url\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BbUrlBareHtml = new(
        @"\[url\](?<u>[^\[]+)\[/url\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static Regex MakeContainer(string tag) =>
        new($@"\[{tag}\](.*?)\[/{tag}\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // ==== Kroste-CSS fuer HtmlPanel ====
    //
    // v1.21.2: universeller `* { color: … }` — HtmlRenderer bekommt den
    // HTML-String OHNE `<html><body>`-Wrap (Nexus liefert nur Body-Fragmente).
    // Body-Selektor greift dann nicht, Text erbt Default-Foreground des
    // Panels (schwarz auf dunklem Card-Background → unsichtbar, User sah
    // nur die blauen Links). Universeller Selektor + zusaetzlicher HTML-
    // Wrap in CreateRichView fixt das.
    private const string KrosteBaseCss = @"
        * { color: #E5E7EB; }
        body, html, div, p, span, li, td, th, dt, dd
             { color: #E5E7EB; font-family: 'Inter', 'Segoe UI', sans-serif;
               font-size: 14px; line-height: 1.55; }
        body { margin: 0; padding: 0; background: transparent; }
        p    { margin: 0 0 10px 0; }
        h1, h2, h3, h4, h5, h6 { color: #E0B14C; margin: 12px 0 6px 0; font-weight: 600; }
        h1 { font-size: 18px; }
        h2 { font-size: 16px; }
        h3 { font-size: 14px; }
        strong, b { font-weight: 600; color: #FFFFFF; }
        em, i { font-style: italic; }
        a    { color: #66C0F4; text-decoration: none; }
        a:hover { text-decoration: underline; }
        code { background: #1F2937; color: #E5E7EB; padding: 1px 4px;
               border-radius: 3px; font-family: 'Consolas', 'Menlo', monospace; }
        pre  { background: #1F2937; padding: 8px; border-radius: 4px;
               overflow: auto; }
        hr   { border: 0; border-top: 1px solid #374151; margin: 12px 0; }
        blockquote { border-left: 3px solid #E0B14C; padding-left: 10px;
                     margin: 8px 0; color: #9CA3AF; }
        ul, ol { padding-left: 22px; margin: 4px 0 10px 0; }
        li     { margin-bottom: 2px; }
        img  { max-width: 100%; height: auto; margin: 4px 0; }
        .center { text-align: center; }
        .right  { text-align: right; }
        .spoiler{ background: #374151; color: #374151; padding: 0 3px;
                  border-radius: 3px; }
        .credit { font-size: 11px; color: #6B7280; font-style: italic; }
    ";

    // ==== Public API ====

    public string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = html;

        s = HtmlBr.Replace(s, "\n");
        s = HtmlPClose.Replace(s, "\n\n");
        s = HtmlAnyTag.Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s);

        s = BbImg.Replace(s, "");
        s = BbUrlWithArg.Replace(s, "$1");
        s = BbUrlBare.Replace(s, "$1");
        s = BbLine.Replace(s, "\n― ― ― ― ― ― ― ― ― ―\n");
        s = BbBr.Replace(s, "\n");

        s = BbListItemClose.Replace(s, "");
        s = BbListItem.Replace(s, "\n•  ");

        foreach (var tag in ContainerTags)
        {
            s = Regex.Replace(s, $@"\[{tag}(?:=[^\]]*)?\](.*?)\[/{tag}\]", "$1",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
        s = BbStandaloneTag.Replace(s, "");
        s = TrailingSpacesBeforeNewline.Replace(s, "\n");
        s = MultiBlankLines.Replace(s, "\n\n");
        return s.Trim();
    }

    public IReadOnlyList<InlineImage> ExtractImages(string html)
    {
        if (string.IsNullOrEmpty(html)) return Array.Empty<InlineImage>();
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

    public string ToHtml(string bbcodeOrHtml)
    {
        if (string.IsNullOrEmpty(bbcodeOrHtml)) return "";
        var s = bbcodeOrHtml;

        // [img …]URL[/img] mit optionaler height. HtmlRenderer versteht
        // <img src="..." style="max-height:100px">.
        s = BbImgWithHtml.Replace(s, m =>
        {
            var url = m.Groups["url"].Value.Trim();
            if (url.Length == 0) return "";
            var hm = HeightAttr.Match(m.Groups["attrs"].Value);
            var style = hm.Success ? $" style=\"max-height:{hm.Groups["h"].Value}px\"" : "";
            return $"<img src=\"{HtmlEscape(url)}\"{style}>";
        });

        // [url=..]Text[/url] und [url]URL[/url]
        s = BbUrlWithArgHtml.Replace(s, m =>
            $"<a href=\"{HtmlEscape(m.Groups["u"].Value.Trim())}\">{m.Groups["t"].Value}</a>");
        s = BbUrlBareHtml.Replace(s, m =>
        {
            var u = m.Groups["u"].Value.Trim();
            return $"<a href=\"{HtmlEscape(u)}\">{HtmlEscape(u)}</a>";
        });

        // Text-Formatierung
        s = BbBold.Replace(s, "<strong>$1</strong>");
        s = BbItalic.Replace(s, "<em>$1</em>");
        s = BbUnderline.Replace(s, "<u>$1</u>");
        s = BbStrike.Replace(s, "<s>$1</s>");
        s = BbSub.Replace(s, "<sub>$1</sub>");
        s = BbSup.Replace(s, "<sup>$1</sup>");
        s = BbCodeTag.Replace(s, "<code>$1</code>");
        s = BbQuote.Replace(s, "<blockquote>$1</blockquote>");
        s = BbSpoiler.Replace(s, "<span class=\"spoiler\">$1</span>");
        s = BbCredit.Replace(s, "<span class=\"credit\">$1</span>");

        // Alignment
        s = BbCenter.Replace(s, "<div class=\"center\">$1</div>");
        s = BbRight.Replace(s, "<div class=\"right\">$1</div>");
        s = BbLeft.Replace(s, "<div>$1</div>");

        // Farbe/Groesse/Font mit inline-style
        s = BbColor.Replace(s, m =>
        {
            var v = m.Groups["v"].Value.Trim().Trim('"', '\'');
            return $"<span style=\"color:{HtmlEscape(v)}\">{m.Groups["c"].Value}</span>";
        });
        s = BbSize.Replace(s, m =>
        {
            var v = m.Groups["v"].Value.Trim().Trim('"', '\'');
            // BBCode-Size ist oft eine Zahl (1–7 relativ), wir mappen grob.
            var px = int.TryParse(v, out var n) ? MapBbSize(n) : v;
            return $"<span style=\"font-size:{px}\">{m.Groups["c"].Value}</span>";
        });
        s = BbFont.Replace(s, m =>
        {
            var v = m.Groups["v"].Value.Trim().Trim('"', '\'');
            return $"<span style=\"font-family:'{HtmlEscape(v)}'\">{m.Groups["c"].Value}</span>";
        });

        // [list] … [*]Item … [/list]  →  <ul>…<li>Item</li>…</ul>
        s = BbListBlock.Replace(s, m =>
        {
            var inner = m.Groups[1].Value;
            // [/*] loeschen, dann [*]…\n zu <li>…</li>
            inner = BbListItemClose.Replace(inner, "");
            var items = Regex.Split(inner, @"\[\*\]", RegexOptions.IgnoreCase);
            var sb = new StringBuilder("<ul>");
            foreach (var raw in items)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                sb.Append("<li>").Append(trimmed).Append("</li>");
            }
            sb.Append("</ul>");
            return sb.ToString();
        });

        // YouTube-Placeholder — wir bauen nur einen klickbaren Link, kein Embed
        // (HtmlRenderer kann keine iframes).
        s = BbYoutube.Replace(s, m =>
        {
            var id = m.Groups["id"].Value.Trim();
            var url = $"https://www.youtube.com/watch?v={HtmlEscape(id)}";
            return $"<a href=\"{url}\">▶ YouTube: {HtmlEscape(id)}</a>";
        });

        // [line] und [br]
        s = BbLine.Replace(s, "<hr>");
        s = BbBr.Replace(s, "<br>");

        // Standalone-[*] ausserhalb [list] — als eigene Zeile mit Bullet
        s = BbListItemClose.Replace(s, "");
        s = BbListItem.Replace(s, "<br>•  ");

        // Restliche unbekannte Standalone-Tags weg.
        s = BbStandaloneTag.Replace(s, "");

        return s;
    }

    public Control CreateRichView(string html)
    {
        if (string.IsNullOrEmpty(html))
            return new TextBlock { Text = "", TextWrapping = TextWrapping.Wrap };

        try
        {
            // v1.21.2: HTML immer in <html><body> wrappen. Nexus liefert
            // rohe Body-Fragmente (nur <div>/<p>-Zeug), ohne diesen Wrap
            // greift der body-CSS-Selektor nicht und der User sieht nur
            // die Elemente die eine explizite Color haben (Links etc.).
            var wrappedHtml = html.Contains("<html", StringComparison.OrdinalIgnoreCase)
                ? html
                : $"<html><body>{html}</body></html>";
            var panel = new HtmlPanel
            {
                Text = wrappedHtml,
                BaseStylesheet = KrosteBaseCss,
                IsSelectionEnabled = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            // Links: HtmlPanel oeffnet http/https-URIs default via OS-Handler.
            // Wenn feineres Handling noetig wird (in-app-Preview etc.),
            // via panel.LinkClicked-Reflection nachziehen — die exakte API-
            // Signatur des HtmlRendererRoutedEventArgs<T>-Wrappers ist
            // undokumentiert und wechselt zwischen Avalonia.HtmlRenderer-
            // Versionen. Der Default deckt aber 100% unseres Use-Cases
            // (Nexus-/BMC-Links im Browser).
            return panel;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "HtmlPanel-Erzeugung fehlgeschlagen — fallback auf Plain-Text-TextBlock");
            return new TextBlock
            {
                Text = ToPlainText(html),
                TextWrapping = TextWrapping.Wrap,
            };
        }
    }

    private static string MapBbSize(int n) => n switch
    {
        <= 1 => "10px",
        2 => "12px",
        3 => "14px",
        4 => "16px",
        5 => "20px",
        6 => "24px",
        _ => "28px",
    };

    private static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("\"", "&quot;")
         .Replace("<", "&lt;").Replace(">", "&gt;");
}
