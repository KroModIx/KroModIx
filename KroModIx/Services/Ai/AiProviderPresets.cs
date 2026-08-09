using System.Collections.Generic;

namespace KroModIx.Services.Ai;

/// <summary>Preset für einen OpenAI-kompatiblen Provider — spart dem User das
/// Google-Suchen der Base-URL und eines sinnvollen Default-Modells.
/// Setzt <see cref="AiProviderType.OpenAiCompatible"/> voraus.</summary>
public sealed record AiProviderPreset(
    string Key,
    string DisplayName,
    string Endpoint,
    string DefaultModel,
    string HelpText);

/// <summary>Kuratierte Presets für <see cref="AiProviderType.OpenAiCompatible"/>.
/// Der User wählt einen Preset im Settings-Dropdown, die Endpoint/Modell-Felder
/// werden gefüllt, er trägt nur noch den API-Key ein.
///
/// <para>Bewusst KEINE eigenen Provider-Klassen — alle diese Services sprechen
/// die OpenAI-Chat-Completions-API (`POST /chat/completions`) und werden vom
/// bestehenden <see cref="OpenAiCompatibleProvider"/> abgedeckt. Ein neuer
/// Preset = ein weiterer Listen-Eintrag hier, keine Code-Änderung sonst.</para></summary>
public static class AiProviderPresets
{
    public static IReadOnlyList<AiProviderPreset> All { get; } = new[]
    {
        new AiProviderPreset(
            Key: "custom",
            DisplayName: "— Frei konfigurieren —",
            Endpoint: "http://localhost:8080/v1",
            DefaultModel: "",
            HelpText: "Beliebiger OpenAI-kompatibler Endpoint (LM Studio, Text-Generation-WebUI, …)."),
        new AiProviderPreset(
            Key: "ninjachat",
            DisplayName: "Ninjachat.ai",
            Endpoint: "https://api.ninjachat.ai/v1",
            DefaultModel: "gpt-4o-mini",
            HelpText: "Multi-Modell-Aggregator (Ninja-Subscription). API-Key aus dem Ninjachat-Account nötig."),
        new AiProviderPreset(
            Key: "venice",
            DisplayName: "Venice.ai",
            Endpoint: "https://api.venice.ai/api/v1",
            DefaultModel: "llama-3.3-70b",
            HelpText: "Dezentral, keine Chat-Logs. API-Key gegen VVV-Token oder USD (venice.ai/settings/api)."),
        new AiProviderPreset(
            Key: "groq",
            DisplayName: "Groq",
            Endpoint: "https://api.groq.com/openai/v1",
            DefaultModel: "llama-3.3-70b-versatile",
            HelpText: "Sehr schnelle Inference (LPU). Free-Tier mit Rate-Limit, sonst günstig pro Token."),
        new AiProviderPreset(
            Key: "together",
            DisplayName: "Together.ai",
            Endpoint: "https://api.together.xyz/v1",
            DefaultModel: "meta-llama/Llama-3.3-70B-Instruct-Turbo",
            HelpText: "Große Modell-Auswahl (Llama, Qwen, Mistral, …) zu günstigen Preisen."),
        new AiProviderPreset(
            Key: "openrouter",
            DisplayName: "OpenRouter",
            Endpoint: "https://openrouter.ai/api/v1",
            DefaultModel: "openai/gpt-4o-mini",
            HelpText: "Multi-Provider-Router (alle großen Anbieter unter einem Key)."),
        new AiProviderPreset(
            Key: "lmstudio",
            DisplayName: "LM Studio (lokal)",
            Endpoint: "http://localhost:1234/v1",
            DefaultModel: "",
            HelpText: "LM Studio Server-Modus. Modell wird in der LM-Studio-App gewählt und geladen."),
    };
}
