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
using Microsoft.Extensions.DependencyInjection;

namespace KroModIx.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly AiSettingsService _ai;
    private readonly AiProviderFactory _aiFactory;
    private readonly SystemHardwareService _hw;
    private readonly IServiceProvider _services;

    public IReadOnlyList<LanguageOption> Languages { get; }
    public IReadOnlyList<AiProviderOption> AiProviders { get; }
    public IReadOnlyList<AiProviderPreset> OpenAiCompatiblePresets { get; }
    public IReadOnlyList<VramOption> VramOptions { get; }

    public ObservableCollection<string> InstalledOllamaModels { get; } = new();
    public ObservableCollection<OllamaModelRowViewModel> RecommendedOllamaModels { get; } = new();

    [ObservableProperty] private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOllamaSection))]
    [NotifyPropertyChangedFor(nameof(ShowAnthropicSection))]
    [NotifyPropertyChangedFor(nameof(ShowOpenAiSection))]
    [NotifyPropertyChangedFor(nameof(ShowGeminiSection))]
    [NotifyPropertyChangedFor(nameof(ShowMistralSection))]
    [NotifyPropertyChangedFor(nameof(ShowOpenAiCompatibleSection))]
    private AiProviderOption? _selectedAiProvider;

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

    [ObservableProperty] private AiProviderPreset? _selectedOpenAiCompatiblePreset;
    [ObservableProperty] private string _aiStatus = "";

    /// <summary>Kurze Zusatzinfo unter der Modell-Combobox („X installiert" /
    /// „Ollama nicht erreichbar" / „Noch nicht geprüft"). Ersetzt den früheren
    /// generischen AiStatus-Text für diese Zeile.</summary>
    [ObservableProperty] private string _installedModelsHint = "Noch nicht geprüft — klick 🔄 oder wähle Ollama, dann wird beim Öffnen automatisch geladen.";

    /// <summary>Getrennt von <see cref="OllamaModel"/>, damit die ComboBox beim
    /// initial-Render mit leerer <see cref="InstalledOllamaModels"/> nicht das
    /// gespeicherte Modell wegräumt (bekanntes Avalonia-ComboBox-Verhalten:
    /// SelectedItem-Bind auf einen Wert der nicht in ItemsSource ist → null).
    /// Der Handler <see cref="OnSelectedInstalledModelChanged"/> kopiert die
    /// User-Auswahl nach <see cref="OllamaModel"/>.
    /// Selbes Muster für alle Cloud-Provider (siehe Pitfalls-Doku).</summary>
    [ObservableProperty] private string? _selectedInstalledModel;

    // Cloud-Provider-Presets: Selected-Preset pro Provider bindet an die
    // ComboBox, Handler kopiert die Auswahl in die eigentliche Model-TextBox.
    [ObservableProperty] private CuratedCloudModel? _selectedAnthropicPreset;
    [ObservableProperty] private CuratedCloudModel? _selectedOpenAiPreset;
    [ObservableProperty] private CuratedCloudModel? _selectedGeminiPreset;
    [ObservableProperty] private CuratedCloudModel? _selectedMistralPreset;

    /// <summary>Für die ComboBox: Union aus kuratierten Presets + live vom Provider
    /// abgefragten Modellen (via <see cref="IAiProvider.ListModelsAsync"/>).
    /// Beim Öffnen der Settings mit gesetztem API-Key wird die Live-Liste
    /// automatisch nachgeladen und mit den Presets verschmolzen — Presets zeigen
    /// ihr reiches Label („Claude Sonnet 4.6 — ausgewogen …"), Live-Modelle die
    /// nicht in den Presets sind zeigen nur die ID (z.B. „claude-3-5-sonnet-20241022").</summary>
    public ObservableCollection<CuratedCloudModel> AnthropicModelOptions { get; } = new();
    public ObservableCollection<CuratedCloudModel> OpenAiModelOptions { get; } = new();
    public ObservableCollection<CuratedCloudModel> GeminiModelOptions { get; } = new();
    public ObservableCollection<CuratedCloudModel> MistralModelOptions { get; } = new();

    [ObservableProperty] private string _anthropicModelsHint = "";
    [ObservableProperty] private string _openAiModelsHint = "";
    [ObservableProperty] private string _geminiModelsHint = "";
    [ObservableProperty] private string _mistralModelsHint = "";

    /// <summary>Guard: unterdrückt die Copy-Handler beim programmatischen Sync
    /// (bei Load und wenn der User in der Model-TextBox tippt sollen die
    /// ComboBox-Selections nicht redundant den TextBox-Wert überschreiben).</summary>
    private bool _suppressSelectedModelSync;

    partial void OnSelectedInstalledModelChanged(string? value)
    {
        if (_suppressSelectedModelSync) return;
        if (!string.IsNullOrWhiteSpace(value)) OllamaModel = value;
    }
    partial void OnSelectedAnthropicPresetChanged(CuratedCloudModel? value)
    {
        if (_suppressSelectedModelSync || value is null) return;
        AnthropicModel = value.Name;
    }
    partial void OnSelectedOpenAiPresetChanged(CuratedCloudModel? value)
    {
        if (_suppressSelectedModelSync || value is null) return;
        OpenAiModel = value.Name;
    }
    partial void OnSelectedGeminiPresetChanged(CuratedCloudModel? value)
    {
        if (_suppressSelectedModelSync || value is null) return;
        GeminiModel = value.Name;
    }
    partial void OnSelectedMistralPresetChanged(CuratedCloudModel? value)
    {
        if (_suppressSelectedModelSync || value is null) return;
        MistralModel = value.Name;
    }

    // Hardware + Modell-Empfehlungen
    [ObservableProperty] private string _detectedHardwareLabel = "wird erkannt …";
    [ObservableProperty] private VramOption? _selectedVramOption;
    [ObservableProperty] private bool _isVramOverride;

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

    public SettingsWindowViewModel(
        AppSettingsService settings,
        AiSettingsService ai,
        AiProviderFactory aiFactory,
        SystemHardwareService hw,
        IServiceProvider services)
    {
        _settings = settings;
        _ai = ai;
        _aiFactory = aiFactory;
        _hw = hw;
        _services = services;

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
            new(AiProviderType.OpenAiCompatible, "OpenAI-kompatibel (Preset/frei)"),
        };
        OpenAiCompatiblePresets = AiProviderPresets.All;
        VramOptions = new List<VramOption>
        {
            new(null,  "🔍  Auto-Detect"),
            new(0,     "💻  Nur CPU"),
            new(4,     "🎮  4 GB VRAM"),
            new(6,     "🎮  6 GB VRAM"),
            new(8,     "🎮  8 GB VRAM"),
            new(10,    "🎮  10 GB VRAM"),
            new(12,    "🎮  12 GB VRAM"),
            new(16,    "🎮  16 GB VRAM"),
            new(24,    "🎮  24 GB VRAM"),
            new(48,    "🚀  48+ GB VRAM"),
        };

        LoadAiIntoUi(_ai.Current);
        SelectedVramOption = VramOptions.FirstOrDefault(v => v.VramGb == _ai.Current.OllamaVramGbOverride) ?? VramOptions[0];
        IsVramOverride = _ai.Current.OllamaVramGbOverride is not null;

        ApiEnabled = settings.Current.ApiEnabled;
        ApiPort = settings.Current.ApiPort <= 0 ? 5100 : settings.Current.ApiPort;
        ApiBearerToken = settings.Current.ApiBearerToken ?? "";

        // Cloud-Model-Options mit kuratierten Presets initial füllen —
        // Live-Modelle werden asynchron beim Öffnen bzw. Provider-Wechsel dazu-
        // gemischt (siehe RefreshCloudModelsAsync).
        PopulateInitial(AnthropicModelOptions, CuratedCloudModels.For(AiProviderType.Anthropic));
        PopulateInitial(OpenAiModelOptions,    CuratedCloudModels.For(AiProviderType.OpenAi));
        PopulateInitial(GeminiModelOptions,    CuratedCloudModels.For(AiProviderType.Gemini));
        PopulateInitial(MistralModelOptions,   CuratedCloudModels.For(AiProviderType.Mistral));

        // Hardware asynchron im Hintergrund erkennen — blockiert das
        // Öffnen der Settings nicht (nvidia-smi kann bis zu 2s dauern).
        _ = DetectHardwareAndPopulateAsync();

        // Provider-spezifisches Preload:
        // - Ollama: /api/tags (still bei Fehler — kein API-Key nötig)
        // - Cloud: /v1/models mit API-Key (still bei fehlendem Key)
        _ = PreloadForActiveProviderAsync();
    }

    private static void PopulateInitial(ObservableCollection<CuratedCloudModel> target,
        IReadOnlyList<CuratedCloudModel> curated)
    {
        target.Clear();
        foreach (var p in curated) target.Add(p);
    }

    private Task PreloadForActiveProviderAsync() => _ai.Current.Provider switch
    {
        AiProviderType.Ollama => LoadOllamaModelsSilentlyAsync(),
        AiProviderType.Anthropic => RefreshCloudModelsAsync(AiProviderType.Anthropic),
        AiProviderType.OpenAi => RefreshCloudModelsAsync(AiProviderType.OpenAi),
        AiProviderType.Gemini => RefreshCloudModelsAsync(AiProviderType.Gemini),
        AiProviderType.Mistral => RefreshCloudModelsAsync(AiProviderType.Mistral),
        _ => Task.CompletedTask,
    };

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;
        var iso = string.IsNullOrEmpty(value.Iso) ? null : value.Iso;
        _settings.Update(s => s.UiCulture = iso);
        LocalizationService.Instance.SetCulture(iso ?? CultureInfo.InvariantCulture.TwoLetterISOLanguageName);
    }

    partial void OnSelectedOpenAiCompatiblePresetChanged(AiProviderPreset? value)
    {
        if (value is null || value.Key == "custom") return;
        OpenAiCompatibleEndpoint = value.Endpoint;
        OpenAiCompatibleModel = value.DefaultModel;
        AiStatus = $"Preset '{value.DisplayName}' übernommen. API-Key ergänzen und speichern.";
    }

    partial void OnSelectedVramOptionChanged(VramOption? value)
    {
        if (value is null) return;
        IsVramOverride = value.VramGb is not null;
        _ = PopulateRecommendationsAsync();
    }

    partial void OnSelectedAiProviderChanged(AiProviderOption? value)
    {
        if (value is null) return;
        _ = value.Type switch
        {
            AiProviderType.Ollama => LoadOllamaModelsSilentlyAsync(),
            AiProviderType.Anthropic => RefreshCloudModelsAsync(AiProviderType.Anthropic),
            AiProviderType.OpenAi => RefreshCloudModelsAsync(AiProviderType.OpenAi),
            AiProviderType.Gemini => RefreshCloudModelsAsync(AiProviderType.Gemini),
            AiProviderType.Mistral => RefreshCloudModelsAsync(AiProviderType.Mistral),
            _ => Task.CompletedTask,
        };
    }

    [RelayCommand]
    private Task RefreshAnthropicModels() => RefreshCloudModelsAsync(AiProviderType.Anthropic, userTriggered: true);
    [RelayCommand]
    private Task RefreshOpenAiModels() => RefreshCloudModelsAsync(AiProviderType.OpenAi, userTriggered: true);
    [RelayCommand]
    private Task RefreshGeminiModels() => RefreshCloudModelsAsync(AiProviderType.Gemini, userTriggered: true);
    [RelayCommand]
    private Task RefreshMistralModels() => RefreshCloudModelsAsync(AiProviderType.Mistral, userTriggered: true);

    /// <summary>Fragt die Live-Modellliste per <see cref="IAiProvider.ListModelsAsync"/>
    /// ab und mischt sie mit den kuratierten Presets in die richtige
    /// <c>XxxModelOptions</c>-Collection. Kuratierte behalten ihr reiches Label;
    /// Live-Modelle die nicht in den Presets sind kriegen nur die ID als Label.
    /// Setzt den Hint-Text unter der ComboBox („5 Modelle vom Anbieter geladen"
    /// oder „API-Key fehlt" bei stille Fehler).</summary>
    private async Task RefreshCloudModelsAsync(AiProviderType provider, bool userTriggered = false)
    {
        var (options, hintSetter) = provider switch
        {
            AiProviderType.Anthropic => (AnthropicModelOptions, (Action<string>)(v => AnthropicModelsHint = v)),
            AiProviderType.OpenAi    => (OpenAiModelOptions,    v => OpenAiModelsHint    = v),
            AiProviderType.Gemini    => (GeminiModelOptions,    v => GeminiModelsHint    = v),
            AiProviderType.Mistral   => (MistralModelOptions,   v => MistralModelsHint   = v),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        var curated = CuratedCloudModels.For(provider);

        if (userTriggered) hintSetter("Modelle werden geladen …");

        // Kein API-Key im aktuellen Buildable-Setting? Nur kuratierte Presets
        // zeigen (die sind ja schon in options), kein Netzwerkaufruf.
        var settings = BuildAiFromUi() with { Provider = provider };
        var cfg = settings.Active;
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            MergeInto(options, curated, liveModels: Array.Empty<string>());
            hintSetter(userTriggered
                ? "API-Key fehlt — Modell-Discovery vom Anbieter überspringen. Nur kuratierte Presets."
                : "");
            return;
        }

        try
        {
            var provInst = _aiFactory.Create(settings);
            if (provInst is null)
            {
                hintSetter("Provider konnte nicht instanziert werden.");
                return;
            }
            var live = await provInst.ListModelsAsync();
            MergeInto(options, curated, live);
            hintSetter(live.Count == 0
                ? "Anbieter lieferte keine Modelle (Key ungültig oder Endpoint blockiert). Nur kuratierte Presets."
                : $"{live.Count} Modelle vom Anbieter geladen + {curated.Count} kuratierte Presets.");
        }
        catch (Exception ex)
        {
            hintSetter($"✗ Fehler beim Abrufen: {ex.Message}. Nur kuratierte Presets.");
        }
        finally
        {
            // Nach Refresh die ComboBox-Selection nochmal auf das aktuell
            // aktive Modell sync-en. Sonst zeigt sie Placeholder weil das
            // Item durch den Clear/Refill neu instanziert wurde und die
            // SelectedItem-Referenz nicht mehr identisch ist.
            SyncCloudSelection(provider);
        }
    }

    private void SyncCloudSelection(AiProviderType provider)
    {
        _suppressSelectedModelSync = true;
        try
        {
            switch (provider)
            {
                case AiProviderType.Anthropic:
                    SelectedAnthropicPreset = AnthropicModelOptions.FirstOrDefault(p => p.Name == AnthropicModel);
                    break;
                case AiProviderType.OpenAi:
                    SelectedOpenAiPreset = OpenAiModelOptions.FirstOrDefault(p => p.Name == OpenAiModel);
                    break;
                case AiProviderType.Gemini:
                    SelectedGeminiPreset = GeminiModelOptions.FirstOrDefault(p => p.Name == GeminiModel);
                    break;
                case AiProviderType.Mistral:
                    SelectedMistralPreset = MistralModelOptions.FirstOrDefault(p => p.Name == MistralModel);
                    break;
            }
        }
        finally { _suppressSelectedModelSync = false; }
    }

    private static void MergeInto(
        ObservableCollection<CuratedCloudModel> target,
        IReadOnlyList<CuratedCloudModel> curated,
        IReadOnlyList<string> liveModels)
    {
        target.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Kuratierte zuerst (mit reichem Label)
        foreach (var c in curated)
        {
            if (seen.Add(c.Name)) target.Add(c);
        }
        // Live-Modelle die noch nicht drin sind — Label = ID
        foreach (var id in liveModels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(id)) target.Add(new CuratedCloudModel(id, id));
        }
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
        SelectedOpenAiCompatiblePreset = OpenAiCompatiblePresets
            .FirstOrDefault(p => string.Equals(p.Endpoint, s.OpenAiCompatible.Endpoint, StringComparison.OrdinalIgnoreCase))
            ?? OpenAiCompatiblePresets[0];

        // ComboBox-Selections initial synchronisieren ohne die Copy-Handler
        // zu triggern (die würden AnthropicModel etc. redundant überschreiben).
        _suppressSelectedModelSync = true;
        try
        {
            SelectedAnthropicPreset = AnthropicModelOptions.FirstOrDefault(p => p.Name == s.Anthropic.Model);
            SelectedOpenAiPreset    = OpenAiModelOptions.FirstOrDefault(p => p.Name == s.OpenAi.Model);
            SelectedGeminiPreset    = GeminiModelOptions.FirstOrDefault(p => p.Name == s.Gemini.Model);
            SelectedMistralPreset   = MistralModelOptions.FirstOrDefault(p => p.Name == s.Mistral.Model);
        }
        finally { _suppressSelectedModelSync = false; }
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
        OllamaVramGbOverride = SelectedVramOption?.VramGb,
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
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        ApiBearerToken = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        ApiStatus = "Token generiert — nicht vergessen zu speichern.";
    }

    [RelayCommand]
    private async Task LoadOllamaModelsAsync()
    {
        InstalledModelsHint = "Lade installierte Modelle …";
        AiStatus = "Ollama-Modelle werden geladen …";
        var ok = await FetchOllamaModelsAsync().ConfigureAwait(true);
        if (ok is null)
        {
            InstalledModelsHint = "✗ Ollama nicht erreichbar (Endpoint prüfen — läuft `ollama serve`?)";
            AiStatus = InstalledModelsHint;
            return;
        }
        InstalledModelsHint = ok.Value == 0
            ? "Keine Modelle installiert — unten aus den Empfehlungen eines herunterladen."
            : $"{ok.Value} installierte Modelle in der Liste — direkt aus dem Dropdown wählen.";
        AiStatus = "";
    }

    /// <summary>Auto-Preload beim Öffnen bzw. Provider-Wechsel. Setzt nur den
    /// Hint-Text und schluckt Fehler still (Ollama nicht gestartet → leere
    /// Liste, aber kein rotes „Fehler"). Der User kann trotzdem via 🔄 manuell
    /// nachladen und sieht dort die klare Fehlermeldung.</summary>
    private async Task LoadOllamaModelsSilentlyAsync()
    {
        var ok = await FetchOllamaModelsAsync().ConfigureAwait(true);
        InstalledModelsHint = ok switch
        {
            null => "Ollama nicht erreichbar — Endpoint prüfen oder 🔄 klicken für Details.",
            0 => "Keine installierten Modelle gefunden — unten aus den Empfehlungen eines herunterladen.",
            _ => $"{ok.Value} installierte Modelle im Dropdown verfügbar.",
        };
    }

    /// <summary>Ruft <c>/api/tags</c> ab, aktualisiert
    /// <see cref="InstalledOllamaModels"/> + `IsInstalled`-Flags in der
    /// Empfehlungs-Liste. Liefert Anzahl gefundener Modelle oder null bei Fehler.</summary>
    private async Task<int?> FetchOllamaModelsAsync()
    {
        try
        {
            var provider = _aiFactory.Create(BuildAiFromUi());
            if (provider is null) return null;
            var models = await provider.ListModelsAsync();
            InstalledOllamaModels.Clear();
            foreach (var m in models.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                InstalledOllamaModels.Add(m);
            foreach (var row in RecommendedOllamaModels)
                row.IsInstalled = models.Any(im => string.Equals(im, row.ModelName, StringComparison.OrdinalIgnoreCase));
            // ComboBox mit dem aktuellen Wert synchronisieren (nur wenn er in
            // der Liste ist) — damit der User sieht welches Modell aktiv ist.
            // Guard verhindert dass unser OnSelectedInstalledModelChanged-Handler
            // OllamaModel redundant überschreibt.
            var match = InstalledOllamaModels.FirstOrDefault(m =>
                string.Equals(m, OllamaModel, StringComparison.OrdinalIgnoreCase));
            _suppressSelectedModelSync = true;
            try { SelectedInstalledModel = match; }
            finally { _suppressSelectedModelSync = false; }
            return models.Count;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Setzt das gegebene Modell als aktives — genutzt vom
    /// „🔘 Aktivieren"-Button in den Empfehlungs-Karten (nur für installierte).</summary>
    [RelayCommand]
    private void ActivateOllamaModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;
        OllamaModel = modelName;
        AiStatus = $"'{modelName}' als aktives Modell gesetzt — nicht vergessen zu speichern.";
    }

    private async Task DetectHardwareAndPopulateAsync()
    {
        var gpu = await _hw.GetGpuAsync().ConfigureAwait(true);
        DetectedHardwareLabel = gpu.VramGb > 0
            ? $"🖥  {gpu.Name} · {gpu.VramGb:0.#} GB VRAM"
            : $"🖥  {gpu.Name} · VRAM unbekannt (bitte manuell wählen)";
        await PopulateRecommendationsAsync().ConfigureAwait(true);
    }

    /// <summary>Baut <see cref="RecommendedOllamaModels"/> anhand der aktuell
    /// wirksamen VRAM-Größe (Override wenn gesetzt, sonst Auto-Detect).</summary>
    private async Task PopulateRecommendationsAsync()
    {
        double vramGb;
        if (SelectedVramOption?.VramGb is double explicitVram)
            vramGb = explicitVram;
        else
        {
            var gpu = await _hw.GetGpuAsync().ConfigureAwait(true);
            vramGb = gpu.VramGb;
        }

        var models = OllamaCuratedModels.RecommendedFor(vramGb);
        RecommendedOllamaModels.Clear();
        foreach (var m in models)
        {
            var row = new OllamaModelRowViewModel(m,
                providerFactory: () => _aiFactory.Create(BuildAiFromUi()) as OllamaProvider,
                onInstalled: name =>
                {
                    if (!InstalledOllamaModels.Contains(name)) InstalledOllamaModels.Add(name);
                });
            row.IsInstalled = InstalledOllamaModels.Any(im => string.Equals(im, m.Name, StringComparison.OrdinalIgnoreCase));
            RecommendedOllamaModels.Add(row);
        }
    }
}

public sealed record LanguageOption(string Iso, string DisplayLabel, string ShortIso);
public sealed record AiProviderOption(AiProviderType Type, string DisplayLabel);

/// <summary>Item im VRAM-Dropdown. <see cref="VramGb"/> = null = Auto-Detect.</summary>
public sealed record VramOption(double? VramGb, string DisplayLabel);
