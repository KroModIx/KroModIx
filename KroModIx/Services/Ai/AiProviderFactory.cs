// Kroste-Standard-Baustein: KI-Multi-Provider-Integration.
// Setup-Anleitung: assets/Ai/README.md
// Kontext: references/ki-integration.md
//
using System.Net.Http;

namespace KroModIx.Services.Ai;

/// <summary>
/// Erzeugt für die aktuellen <see cref="AiSettings"/> den passenden
/// <see cref="IAiProvider"/>. Anbieter-Auswahl per Enum-Switch (Kroste-Standard,
/// nach Allpaca-Vorbild). Mistral wird bewusst über den OpenAI-kompatiblen
/// Provider bedient, da Mistrals API vollständig OpenAI-Chat-Completions-
/// konform ist — spart uns eine separate Klasse.
/// </summary>
public sealed class AiProviderFactory
{
    private readonly IHttpClientFactory _httpFactory;

    public AiProviderFactory(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    /// <summary>Liefert einen konfigurierten Provider oder <c>null</c>, wenn
    /// <see cref="AiSettings.Provider"/> auf <see cref="AiProviderType.None"/>
    /// steht (oder der aktive Provider Pflichtfelder wie API-Key vermissen
    /// lässt).</summary>
    public IAiProvider? Create(AiSettings settings)
    {
        var cfg = settings.Active;
        var http = _httpFactory.CreateClient("ai");

        // Cloud-Provider brauchen Timeouts, die auch längere Rate-Limit-Waits
        // überleben. Ollama bekommt einen sehr langen Timeout, weil grössere
        // Batches an lokalen Modellen mehrere Minuten dauern können. Mit
        // grossen Modellen (14B+) und langen Prompts (Ren'Py-Descriptions
        // mit Changelog) waren die alten 10 Min zu knapp — CompleteAsync
        // nutzt jetzt Streaming (ResponseHeadersRead), Timeout gilt damit
        // nur bis first-byte, die eigentliche Generierung ist idle-open.
        // Trotzdem 30 Min als Safety-Net fuer Non-Streaming-Paths.
        http.Timeout = settings.Provider == AiProviderType.Ollama
            ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(2);

        return settings.Provider switch
        {
            AiProviderType.None => null,
            AiProviderType.Ollama => new OllamaProvider(http, cfg.Endpoint, cfg.Model),
            AiProviderType.Anthropic => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new AnthropicProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey),
            AiProviderType.OpenAi => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new OpenAiCompatibleProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey, "OpenAI"),
            AiProviderType.Gemini => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new GeminiProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey),
            AiProviderType.Mistral => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new OpenAiCompatibleProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey, "Mistral"),
            AiProviderType.OpenAiCompatible => new OpenAiCompatibleProvider(
                http, cfg.Endpoint, cfg.Model, cfg.ApiKey, "OpenAI-kompatibel"),
            _ => null,
        };
    }

    /// <summary>Baut nur den Ollama-Provider auf, unabhängig von der aktuell
    /// gewählten Provider-Auswahl. Für den Modell-Pull im Einstellungen-Fenster
    /// gedacht, damit man Ollama-Modelle auch dann ziehen kann, wenn ein Cloud-
    /// Provider aktiv ist.</summary>
    public OllamaProvider CreateOllama(AiSettings settings)
    {
        var http = _httpFactory.CreateClient("ai-pull");
        http.Timeout = TimeSpan.FromHours(1); // Pull kann bei 4-GB-Modellen dauern.
        return new OllamaProvider(http, settings.Ollama.Endpoint, settings.Ollama.Model);
    }
}
