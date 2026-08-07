using System.Text.RegularExpressions;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.LayoutRenderers.Wrappers;

namespace ModManager.Logging;

/// <summary>
/// Kroste-Standard: maskiert Passwörter/Tokens/Credentials in Log-Ausgaben.
/// Registriert als <c>${masked:inner=...}</c> in der nlog.config. Secrets dürfen
/// niemals im Klartext im Log landen — auch nicht auf Trace-Level.
/// </summary>
[LayoutRenderer("masked")]
[ThreadAgnostic]
public sealed class MaskingLayoutRenderer : WrapperLayoutRendererBase
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new Regex(@"(?i)(password|passwort|token|secret|api[_-]?key|apikey|authorization|bearer|pwd)(\s*[:=]\s*""?)([^""\s,;)]+)",
            RegexOptions.Compiled), "$1$2***"),
        (new Regex(@"(?i)(://[^:/@\s]+:)([^@\s]+)(@)", RegexOptions.Compiled), "$1***$3"),
    ];

    protected override string Transform(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (pattern, replacement) in Rules)
            text = pattern.Replace(text, replacement);
        return text;
    }

    /// <summary>Einmalig beim App-Start registrieren, BEVOR der erste Logger benutzt wird.</summary>
    public static void Register() =>
        LogManager.Setup().SetupExtensions(ext =>
            ext.RegisterLayoutRenderer<MaskingLayoutRenderer>("masked"));
}
