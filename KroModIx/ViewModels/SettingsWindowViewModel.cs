using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Localization;
using KroModIx.Plugin.Contracts;
using KroModIx.Services;
using KroModIx.Services.Ai;

namespace KroModIx.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly AiSettingsService _ai;
    private readonly AiProviderFactory _aiFactory;

    public IReadOnlyList<LanguageOption> Languages { get; }
    public IReadOnlyList<AiProviderOption> AiProviders { get; }
    public ObservableCollection<string> InstalledOllamaModels { get; } = new();
    public IReadOnlyList<OllamaCuratedModel> RecommendedOllamaModels { get; } = OllamaCuratedModels.All;

    [ObservableProperty] private LanguageOption? _selectedLanguage;

    // KI-Provider-Auswahl
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOllamaSection))]
    [NotifyPropertyChangedFor(nameof(ShowAnthropicSection))]
    [NotifyPropertyChangedFor(nameof(ShowOpenAiSection))]
    [NotifyPropertyChangedFor(nameof(ShowGeminiSection))]
    [NotifyPropertyChangedFor(nameof(ShowMistralSection))]
    [NotifyPropertyChangedFor(nameof(ShowOpenAiCompatibleSection))]
    private AiProviderOption? _selectedAiProvider;

    // Per-Provider-Felder (nur eines gleichzeitig sichtbar)
    [ObservableProperty] private string _ollamaEndpoint = "";
    [ObservableProperty] private string _ollamaModel = "";
    [ObservableProperty] private string _anthropicEndpoint = "";
    [ObservableProperty] private string _anthropicModel = "";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _openAiEndpoint = "";
    [ObservableProperty] private string _openAiModel = "";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _geminiEndpoint = "";
    [ObservableProperty] private string _geminiModel = "";
    [ObservableProperty] private string _geminiApiKey = "";
    [ObservableProperty] private string _mistralEndpoint = "";
    [ObservableProperty] private string _mistralModel = "";
    [ObservableProperty] private string _mistralApiKey = "";
    [ObservableProperty] private string _openAiCompatibleEndpoint = "";
    [ObservableProperty] private string _openAiCompatibleModel = "";
    [ObservableProperty] private string _openAiCompatibleApiKey = "";

    [ObservableProperty] private string _aiStatus = "";

    // REST-API-Sektion
    [ObservableProperty] private bool _apiEnabled;
    [ObservableProperty] private int _apiPort = 5100;
    [ObservableProperty] private string _apiBearerToken = "";
    [ObservableProperty] private string _apiStatus = "";

    public bool ShowOllamaSection => SelectedAiProvider?.Type == AiProviderType.Ollama;
    public bool ShowAnthropicSection => SelectedAiProvider?.Type == AiProviderType.Anthropic;
    public bool ShowOpenAiSection => SelectedAiProvider?.Type == AiProviderType.OpenAi;
    public bool ShowGeminiSection => SelectedAiProvider?.Type == AiProviderType.Gemini;
    public bool ShowMistralSection => SelectedAiProvider?.Type == AiProviderType.Mistral;
    public bool ShowOpenAiCompatibleSection => SelectedAiProvider?.Type == AiProviderType.OpenAiCompatible;

    public SettingsWindowViewModel(AppSettingsService settings, AiSettingsService ai, AiProviderFactory aiFactory)
    {
        _settings = settings;
        _ai = ai;
        _aiFactory = aiFactory;

        Languages = new List<LanguageOption>
        {
            new("", "System / Auto", ""),
        }.Concat(LocalizationService.SupportedCultures
                .Select(c => new LanguageOption(c.Iso, $"{c.Flag}  {c.Display}", c.Iso)))
            .ToList();

        var current = settings.Current.UiCulture ?? "";
        SelectedLanguage = Languages.FirstOrDefault(l => l.Iso == current) ?? Languages[0];

        AiProviders = new List<AiProviderOption>
        {
            new(AiProviderType.Ollama, "Ollama (lokal)"),
            new(AiProviderType.Anthropic, "Anthropic Claude"),
            new(AiProviderType.OpenAi, "OpenAI ChatGPT"),
            new(AiProviderType.Gemini, "Google Gemini"),
            new(AiProviderType.Mistral, "Mistral"),
            new(AiProviderType.OpenAiCompatible, "OpenAI-kompatibel (frei)"),
        };
        LoadAiIntoUi(_ai.Current);

        ApiEnabled = settings.Current.ApiEnabled;
        ApiPort = settings.Current.ApiPort <= 0 ? 5100 : settings.Current.ApiPort;
        ApiBearerToken = settings.Current.ApiBearerToken ?? "";
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;
        var iso = string.IsNullOrEmpty(value.Iso) ? null : value.Iso;
        _settings.Update(s => s.UiCulture = iso);
        LocalizationService.Instance.SetCulture(iso ?? CultureInfo.InvariantCulture.TwoLetterISOLanguageName);
    }

    private void LoadAiIntoUi(AiSettings s)
    {
        SelectedAiProvider = AiProviders.FirstOrDefault(p => p.Type == s.Provider) ?? AiProviders[0];
        OllamaEndpoint = s.Ollama.Endpoint;      OllamaModel = s.Ollama.Model;
        AnthropicEndpoint = s.Anthropic.Endpoint; AnthropicModel = s.Anthropic.Model; AnthropicApiKey = s.Anthropic.ApiKey ?? "";
        OpenAiEndpoint = s.OpenAi.Endpoint;       OpenAiModel = s.OpenAi.Model;       OpenAiApiKey = s.OpenAi.ApiKey ?? "";
        GeminiEndpoint = s.Gemini.Endpoint;       GeminiModel = s.Gemini.Model;       GeminiApiKey = s.Gemini.ApiKey ?? "";
        MistralEndpoint = s.Mistral.Endpoint;     MistralModel = s.Mistral.Model;     MistralApiKey = s.Mistral.ApiKey ?? "";
        OpenAiCompatibleEndpoint = s.OpenAiCompatible.Endpoint;
        OpenAiCompatibleModel = s.OpenAiCompatible.Model;
        OpenAiCompatibleApiKey = s.OpenAiCompatible.ApiKey ?? "";
    }

    private AiSettings BuildAiFromUi() => _ai.Current with
    {
        Provider = SelectedAiProvider?.Type ?? AiProviderType.Ollama,
        Ollama    = new AiProviderConfig(OllamaEndpoint.Trim(), OllamaModel.Trim(), null),
        Anthropic = new AiProviderConfig(AnthropicEndpoint.Trim(), AnthropicModel.Trim(), NullIfEmpty(AnthropicApiKey)),
        OpenAi    = new AiProviderConfig(OpenAiEndpoint.Trim(), OpenAiModel.Trim(), NullIfEmpty(OpenAiApiKey)),
        Gemini    = new AiProviderConfig(GeminiEndpoint.Trim(), GeminiModel.Trim(), NullIfEmpty(GeminiApiKey)),
        Mistral   = new AiProviderConfig(MistralEndpoint.Trim(), MistralModel.Trim(), NullIfEmpty(MistralApiKey)),
        OpenAiCompatible = new AiProviderConfig(
            OpenAiCompatibleEndpoint.Trim(), OpenAiCompatibleModel.Trim(), NullIfEmpty(OpenAiCompatibleApiKey)),
    };

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [RelayCommand]
    private void SaveAi()
    {
        _ai.Update(BuildAiFromUi());
        AiStatus = "KI-Einstellungen gespeichert.";
    }

    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        AiStatus = "Verbindung wird getestet …";
        try
        {
            var provider = _aiFactory.Create(BuildAiFromUi());
            if (provider is null) { AiStatus = "✗ Kein Provider konfiguriert."; return; }
            var ok = await provider.IsAvailableAsync();
            AiStatus = ok ? $"✓ {provider.Name} erreichbar." : $"✗ {provider.Name} antwortet nicht.";
        }
        catch (Exception ex) { AiStatus = $"✗ Fehler: {ex.Message}"; }
    }

    [RelayCommand]
    private void SaveApi()
    {
        _settings.Update(s =>
        {
            s.ApiEnabled = ApiEnabled;
            s.ApiPort = ApiPort <= 0 ? 5100 : ApiPort;
            s.ApiBearerToken = NullIfEmpty(ApiBearerToken);
        });
        ApiStatus = ApiEnabled
            ? "API-Einstellungen gespeichert. Neustart nötig damit Kestrel neu gebunden wird."
            : "API-Einstellungen gespeichert. API bleibt beim nächsten Start deaktiviert.";
    }

    [RelayCommand]
    private void GenerateApiToken()
    {
        // 32 Bytes = 256 Bit Entropie, Base64Url ohne Padding = 43 Zeichen.
        // Reicht bequem für einen statischen Bearer-Token — kein JWT nötig.
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        ApiBearerToken = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        ApiStatus = "Token generiert — nicht vergessen zu speichern.";
    }

    [RelayCommand]
    private async Task LoadOllamaModelsAsync()
    {
        AiStatus = "Ollama-Modelle werden geladen …";
        try
        {
            var provider = _aiFactory.Create(BuildAiFromUi());
            if (provider is null) { AiStatus = "✗ Kein Provider."; return; }
            var models = await provider.ListModelsAsync();
            InstalledOllamaModels.Clear();
            foreach (var m in models.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                InstalledOllamaModels.Add(m);
            AiStatus = models.Count == 0
                ? "Keine Modelle installiert. Mit `ollama pull <name>` im Terminal laden."
                : $"{models.Count} Ollama-Modelle gefunden.";
        }
        catch (Exception ex) { AiStatus = $"✗ Fehler: {ex.Message}"; }
    }
}

public sealed record LanguageOption(string Iso, string DisplayLabel, string ShortIso);
public sealed record AiProviderOption(AiProviderType Type, string DisplayLabel);
