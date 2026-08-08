using System;
using System.Threading;
using System.Threading.Tasks;
using ModManager.PluginContracts;
using ModManager.Services.Ai;

namespace ModManager.Services.Plugins;

/// <summary>
/// Adapter zwischen dem Contract <see cref="IAiService"/> (was Plugins sehen)
/// und dem Kroste-KI-Baukasten (<see cref="AiSettingsService"/> +
/// <see cref="AiProviderFactory"/>). Wird als Singleton im Host registriert
/// und an alle Plugins durchgereicht. Nutzt bei jedem Aufruf die aktuell
/// gespeicherten Settings — wenn der User in den Host-Einstellungen den
/// Provider wechselt, greift das ohne Plugin-Restart.
/// </summary>
public sealed class HostAiServiceImpl : IAiService
{
    private readonly AiSettingsService _settings;
    private readonly AiProviderFactory _factory;

    public HostAiServiceImpl(AiSettingsService settings, AiProviderFactory factory)
    {
        _settings = settings;
        _factory = factory;
    }

    public string ProviderInfo
    {
        get
        {
            var s = _settings.Current;
            return $"{s.Provider} · {ActiveModel(s)}";
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var provider = _factory.Create(_settings.Current);
            if (provider is null) return false;
            return await provider.IsAvailableAsync(ct).ConfigureAwait(false);
        }
        catch { return false; }
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt,
        CancellationToken ct = default)
    {
        var provider = _factory.Create(_settings.Current)
            ?? throw new InvalidOperationException(
                "KI-Provider nicht konfiguriert — bitte in den ModManager-Einstellungen einrichten.");
        return await provider.CompleteAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
    }

    private static string ActiveModel(AiSettings s) => s.Provider switch
    {
        AiProviderType.Ollama => s.Ollama.Model,
        AiProviderType.Anthropic => s.Anthropic.Model,
        AiProviderType.OpenAi => s.OpenAi.Model,
        AiProviderType.Gemini => s.Gemini.Model,
        AiProviderType.Mistral => s.Mistral.Model,
        AiProviderType.OpenAiCompatible => s.OpenAiCompatible.Model,
        _ => "?",
    };
}
