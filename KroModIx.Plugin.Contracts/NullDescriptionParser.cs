using System;
using System.Collections.Generic;

namespace KroModIx.Plugin.Contracts;

/// <summary>Default-Impl fuer Hosts &lt; v1.19.0 die den Contract nicht
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
}
