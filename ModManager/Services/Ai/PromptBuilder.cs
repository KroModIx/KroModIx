using System.Collections.Generic;
using System.Text.Json;

namespace ModManager.Services.Ai;

/// <summary>
/// Für ModManager unbenutzt — der Kroste-KI-Baukasten enthält eine
/// projektspezifische Translation-Pipeline (RenPack-Herkunft:
/// <c>TranslateBatchAsync</c> mit strukturiertem JSON-Prompt). Der
/// Baukasten baut das im Provider ein, wir brauchen es nicht.
/// Der Stub hier hält nur den Baukasten kompilierbar; die generische
/// <see cref="IAiProvider.CompleteAsync"/> ist das, was Plugins
/// tatsächlich benutzen.
/// </summary>
internal static class PromptBuilder
{
    public static string System(string _) => "";
    public static string User(IReadOnlyList<string> _) => "{}";
    public static IReadOnlyDictionary<string, string> ParseTranslations(string content)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
            return d ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }
}
