using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace KroModIx.Plugin.Contracts;

/// <summary>Default-Impl fuer Hosts &lt; v1.20.0 die den Contract nicht
/// implementieren. Passthrough — der Aufrufer sieht raw HTML/BBCode,
/// aber das Plugin crasht nicht. Erzieht Plugin-Autoren die auf einen
/// aelteren Host pinnen dazu, die MinHostVersion zu erhoehen wenn sie
/// vernuenftige Descriptions rendern wollen.</summary>
public sealed class NullDescriptionParser : IDescriptionParser
{
    public static readonly NullDescriptionParser Instance = new();
    private NullDescriptionParser() { }

    public string ToPlainText(string html) => html ?? "";

    public IReadOnlyList<InlineImage> ExtractImages(string html) =>
        Array.Empty<InlineImage>();

    public string ToHtml(string bbcodeOrHtml) => bbcodeOrHtml ?? "";

    public Control CreateRichView(string html) =>
        new TextBlock { Text = html ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}
