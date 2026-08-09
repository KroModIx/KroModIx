using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>
/// Vom Host bereitgestellte KI-Abstraktion. Plugins rufen <see cref="CompleteAsync"/>
/// mit ihrem eigenen System- und User-Prompt und bekommen die Text-Antwort
/// des User-konfigurierten Providers (Ollama/Anthropic/OpenAI/Gemini/Mistral/
/// OpenAI-kompatibel). Provider-Config, API-Keys, Modell-Auswahl und Setup-UI
/// liegen zentral im Host — Plugin muss nichts davon wissen.
///
/// <para>Bei nicht-konfiguriertem Provider (kein Endpoint/Modell gesetzt)
/// werfen die Methoden <see cref="System.InvalidOperationException"/>. Plugin
/// sollte vor dem Aufruf <see cref="IsAvailableAsync"/> prüfen und dem Nutzer
/// einen Hinweis auf die Host-Einstellungen geben, falls false.</para>
/// </summary>
public interface IAiService
{
    /// <summary>Kurzer Anzeige-Name des aktuell aktiven Providers + Modells,
    /// z.B. „Ollama (llama3.2)" — für Statusbar oder Toast.</summary>
    string ProviderInfo { get; }

    /// <summary>True wenn Provider erreichbar (Ollama: /api/tags antwortet;
    /// Cloud: gültiger Key + Modell konfiguriert). Nicht cachen — der Nutzer
    /// kann die Config zwischen zwei Aufrufen ändern.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Einzelne Completion: System-Prompt (Rolle/Anweisung) +
    /// User-Prompt (die eigentliche Anfrage) → String-Antwort. Der Aufrufer
    /// parst die Antwort selbst (z.B. JSON extrahieren wenn im System-Prompt
    /// JSON angefordert).</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
