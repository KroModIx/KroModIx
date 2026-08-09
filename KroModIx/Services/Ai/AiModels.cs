// Kroste-Standard-Baustein: KI-Multi-Provider-Integration.
// Setup-Anleitung: assets/Ai/README.md
// Kontext: references/ki-integration.md
//
namespace KroModIx.Services.Ai;

/// <summary>KI-Anbieter für die Variablen-Übersetzung. Ollama läuft lokal ohne
/// API-Key; die Cloud-Anbieter brauchen jeweils einen persönlichen Key, den die
/// App verschlüsselt speichert (siehe <see cref="SecretProtection"/>).</summary>
public enum AiProviderType
{
    /// <summary>Übersetzung deaktiviert.</summary>
    None,
    /// <summary>Lokales Ollama (Default — datenschutzfreundlich, kein API-Key).</summary>
    Ollama,
    /// <summary>Anthropic Claude über die native Messages-API.</summary>
    Anthropic,
    /// <summary>OpenAI ChatGPT über die Chat-Completions-API.</summary>
    OpenAi,
    /// <summary>Google Gemini über die Generative-Language-API.</summary>
    Gemini,
    /// <summary>Mistral über die (OpenAI-kompatible) Chat-Completions-API.</summary>
    Mistral,
    /// <summary>Generischer OpenAI-kompatibler Anbieter (Groq, OpenRouter,
    /// LM Studio, …) mit frei konfigurierbarer Base-URL.</summary>
    OpenAiCompatible,
}

/// <summary>Provider-spezifische Baustellen — Endpoint, Modellname, API-Key. Wird
/// in <see cref="AiSettings"/> pro Anbieter separat gehalten, damit der Nutzer
/// zwischen Anbietern wechseln kann, ohne die Werte neu einzugeben.</summary>
public sealed record AiProviderConfig(string Endpoint, string Model, string? ApiKey = null);

/// <summary>Persistente KI-Konfiguration der App. Die API-Keys werden vor dem
/// Speichern per <see cref="SecretProtection"/> verschlüsselt und liegen NICHT
/// im Klartext in der JSON-Datei.</summary>
public sealed record AiSettings(
    AiProviderType Provider,
    string TargetLanguage,
    AiProviderConfig Ollama,
    AiProviderConfig Anthropic,
    AiProviderConfig OpenAi,
    AiProviderConfig Gemini,
    AiProviderConfig Mistral,
    AiProviderConfig OpenAiCompatible,
    /// <summary>Manueller VRAM-Override in GB, wenn die Auto-Detection nichts
    /// Sinnvolles liefert (kein nvidia-smi, keine Lookup-Entsprechung).
    /// <c>null</c> = Auto-Detect. <c>0</c> = ausdrücklich „nur CPU".</summary>
    double? OllamaVramGbOverride = null)
{
    /// <summary>Hol die Config des aktuell ausgewählten Anbieters.</summary>
    public AiProviderConfig Active => Provider switch
    {
        AiProviderType.Ollama => Ollama,
        AiProviderType.Anthropic => Anthropic,
        AiProviderType.OpenAi => OpenAi,
        AiProviderType.Gemini => Gemini,
        AiProviderType.Mistral => Mistral,
        AiProviderType.OpenAiCompatible => OpenAiCompatible,
        _ => Ollama,
    };

    /// <summary>Sinnvolle Defaults für frische Installationen (Ollama lokal,
    /// keine Keys, System-Sprache).</summary>
    public static AiSettings Default => new(
        Provider: AiProviderType.None,
        TargetLanguage: DetectSystemLanguageName(),
        Ollama:            AiDefaults.Config(AiProviderType.Ollama),
        Anthropic:         AiDefaults.Config(AiProviderType.Anthropic),
        OpenAi:            AiDefaults.Config(AiProviderType.OpenAi),
        Gemini:            AiDefaults.Config(AiProviderType.Gemini),
        Mistral:           AiDefaults.Config(AiProviderType.Mistral),
        OpenAiCompatible:  AiDefaults.Config(AiProviderType.OpenAiCompatible),
        OllamaVramGbOverride: null);

    private static string DetectSystemLanguageName()
    {
        var iso = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return iso switch
        {
            "de" => "Deutsch",
            "en" => "Englisch",
            "fr" => "Französisch",
            "es" => "Spanisch",
            "it" => "Italienisch",
            "ru" => "Russisch",
            "pt" => "Portugiesisch",
            "nl" => "Niederländisch",
            "pl" => "Polnisch",
            "ja" => "Japanisch",
            "zh" => "Chinesisch",
            _ => "Deutsch",
        };
    }
}

/// <summary>Provider-spezifische Standard-Endpunkte und -Modelle. Ausgangsbasis
/// für neue Installationen; jeder Wert lässt sich im Einstellungen-Fenster
/// überschreiben.</summary>
public static class AiDefaults
{
    public static AiProviderConfig Config(AiProviderType p) => new(
        Endpoint: Endpoint(p),
        Model: Model(p),
        ApiKey: null);

    public static string Endpoint(AiProviderType p) => p switch
    {
        AiProviderType.Ollama            => "http://localhost:11434",
        AiProviderType.Anthropic         => "https://api.anthropic.com/v1",
        AiProviderType.OpenAi            => "https://api.openai.com/v1",
        AiProviderType.Gemini            => "https://generativelanguage.googleapis.com/v1beta",
        AiProviderType.Mistral           => "https://api.mistral.ai/v1",
        AiProviderType.OpenAiCompatible  => "http://localhost:8080/v1",
        _ => "",
    };

    /// <summary>Sinnvolle Standard-Modelle pro Provider (Stand: 2026-07). Beim
    /// Update dieser Liste auch <c>OllamaCuratedModels</c> und die Modell-
    /// Vorschläge im Einstellungen-Fenster nachziehen.</summary>
    public static string Model(AiProviderType p) => p switch
    {
        AiProviderType.Ollama            => "gemma3:1b",
        AiProviderType.Anthropic         => "claude-haiku-4-5",
        AiProviderType.OpenAi            => "gpt-4o-mini",
        AiProviderType.Gemini            => "gemini-2.0-flash",
        AiProviderType.Mistral           => "mistral-small-latest",
        AiProviderType.OpenAiCompatible  => "",
        _ => "",
    };
}

/// <summary>Ein einzelnes Event aus Ollamas NDJSON-Antwort von POST /api/pull.
/// <c>Completed</c> und <c>Total</c> sind nur während der Download-Phase gesetzt.</summary>
public sealed record OllamaPullEvent(
    string Status,
    long? Completed,
    long? Total,
    string? Digest,
    bool IsError = false,
    string? ErrorMessage = null);

/// <summary>Kuratierte Modellvorschläge für Ollama. <see cref="MinVramGb"/>/<see cref="MaxVramGb"/>
/// beschreiben in welchem VRAM-Fenster das Modell komfortabel läuft — <see cref="SystemHardwareService"/>
/// liefert die aktuelle GPU-Größe, die Settings-UI sortiert/highlightet danach.
/// Kein VRAM (Integrated / CPU-only) → nur Modelle mit <c>MinVramGb ≤ 0</c> anzeigen.</summary>
public sealed record OllamaCuratedModel(
    string Name,
    string ApproxSize,
    string Description,
    double MinVramGb,
    double MaxVramGb);

public static class OllamaCuratedModels
{
    /// <summary>Kuratiert 2026-Q3. Bei Update: Ollama-Model-Registry checken,
    /// nur Modelle die für Modmanager-Use-Cases (Übersetzung, kurze Zusammenfassung,
    /// Mod-Beschreibung) taugen — kein Coding, kein Reasoning-heavy.</summary>
    public static IReadOnlyList<OllamaCuratedModel> All { get; } = new[]
    {
        // CPU-only / tiny GPUs
        new OllamaCuratedModel("gemma3:1b",         "~815 MB",  "Sehr klein, sehr schnell — läuft auf CPU. Default für alte Rechner.",   0,   3),
        new OllamaCuratedModel("qwen2.5:3b",        "~1.9 GB",  "3B, gute Übersetzungsqualität für die Größe.",                          0,   4),
        new OllamaCuratedModel("llama3.2:3b",       "~2.0 GB",  "Meta Llama 3.2, ausgewogen.",                                          0,   4),
        new OllamaCuratedModel("phi3.5:3.8b",       "~2.3 GB",  "Microsoft Phi-3.5, stark bei mehrsprachigem Text.",                     0,   4),
        // 4-8 GB
        new OllamaCuratedModel("llama3.1:8b",       "~4.7 GB",  "Meta Llama 3.1 8B — solide All-Round.",                                 6,  10),
        new OllamaCuratedModel("qwen2.5:7b",        "~4.4 GB",  "Qwen 2.5 7B, spitze bei Übersetzung DE/EN.",                            6,  10),
        new OllamaCuratedModel("mistral:7b",        "~4.1 GB",  "Mistral 7B, breiter Sprachschatz.",                                     6,  10),
        // 8-16 GB
        new OllamaCuratedModel("qwen2.5:14b",       "~9.0 GB",  "Qwen 2.5 14B — deutlich bessere Qualität als 7B.",                     10,  16),
        new OllamaCuratedModel("gemma3:12b",        "~7.5 GB",  "Google Gemma3 12B, mehrsprachig stark.",                                8,  16),
        // 16-24 GB
        new OllamaCuratedModel("qwen2.5:32b",       "~20 GB",   "Qwen 2.5 32B — für längere Zusammenfassungen.",                        18,  32),
        new OllamaCuratedModel("gemma3:27b",        "~17 GB",   "Google Gemma3 27B, Balance zwischen Qualität und Speed.",              16,  32),
        // 24+ GB (Enthusiast / Workstation)
        new OllamaCuratedModel("llama3.3:70b",      "~43 GB",   "Meta Llama 3.3 70B — Quantized Q4. Für Multi-GPU / 48+ GB VRAM.",      40, 999),
        new OllamaCuratedModel("qwen2.5:72b",       "~47 GB",   "Qwen 2.5 72B, spitze Qualität.",                                       40, 999),
    };

    /// <summary>Filtert die Liste nach der verfügbaren GPU-VRAM. Bei
    /// <c>vramGb ≤ 0</c> (kein dediziertes VRAM) → nur die CPU-tauglichen Kleinstmodelle.</summary>
    public static IReadOnlyList<OllamaCuratedModel> RecommendedFor(double vramGb)
    {
        if (vramGb <= 0)
            return All.Where(m => m.MinVramGb <= 0).ToList();

        // 20% Toleranz nach oben — Q4-Quants können auf leicht kleinerer VRAM laufen.
        var maxComfortable = vramGb * 1.2;
        return All
            .Where(m => m.MinVramGb <= maxComfortable && m.MaxVramGb >= vramGb * 0.7)
            .ToList();
    }
}
