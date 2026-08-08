// Kroste-Standard-Baustein: KI-Multi-Provider-Integration.
// Vor dem Kompilieren "ModManager" durch den echten Projektnamen ersetzen.
// Setup-Anleitung: assets/Ai/README.md
// Kontext: references/ki-integration.md
//
namespace ModManager.Services.Ai;

/// <summary>Provider-Abstraktion für KI-Übersetzungen von Variablennamen.
/// v0.4a hat nur eine Implementierung (Ollama); Anthropic/OpenAI/Gemini folgen
/// in v0.4b nach Allpaca-Vorbild.</summary>
public interface IAiProvider
{
    /// <summary>Kurzer Anzeige-Name des Providers (für Logs und UI).</summary>
    string Name { get; }

    /// <summary>Prüft ohne Übersetzungs-Anfrage, ob der Provider ansprechbar ist
    /// (z. B. läuft ein Ollama-Server auf dem Endpoint?).</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Listet die verfügbaren Modelle (Ollama: /api/tags; für andere
    /// Provider vorerst eine kuratierte Liste).</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Übersetzt eine Charge Variablennamen in die Zielsprache. Die
    /// Rückgabe ist ein Dict <c>{originalName → menschenlesbare Beschreibung}</c>.
    /// Für Namen, für die die KI keine sinnvolle Antwort liefert, fehlt der Key.
    /// <br/><br/>(RenPack-spezifische Alt-API. In ModManager NICHT verwendet
    /// — Plugins nutzen <see cref="CompleteAsync"/>.)</summary>
    Task<IReadOnlyDictionary<string, string>> TranslateBatchAsync(
        IReadOnlyList<string> variableNames,
        string targetLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>Generische Chat-Completion für Plugin-Aufrufer via
    /// <c>IHostServices.Ai.CompleteAsync</c>. Der Aufrufer baut System- und
    /// User-Prompt selbst; die Antwort ist ein einzelner Text-String
    /// (kein JSON-Zwang).</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default);
}
