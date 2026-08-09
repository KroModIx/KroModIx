using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Services.Ai;
using NLog;

namespace KroModIx.ViewModels;

/// <summary>Eine Zeile in der Modell-Empfehlungsliste im Einstellungen-Fenster.
/// Kapselt die Download-Logik über <see cref="OllamaProvider.PullAsync"/> mit
/// Progress-Streaming; Buttons/Progress-Bar binden direkt an dieses VM.
///
/// <para>Wird von <see cref="SettingsWindowViewModel"/> erzeugt und mit einem
/// factory-Delegate versorgt, der pro Download-Klick einen frischen
/// <see cref="OllamaProvider"/> liefert (aus den aktuellen Endpoint-Feldern
/// der Settings-UI).</para></summary>
public sealed partial class OllamaModelRowViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Func<OllamaProvider?> _providerFactory;
    private readonly Action<string>? _onInstalled;
    private CancellationTokenSource? _cts;

    public string ModelName { get; }
    public string ApproxSize { get; }
    public string Description { get; }
    public string VramRangeLabel { get; }

    /// <summary>true wenn das Modell bereits in <c>/api/tags</c> auftaucht —
    /// dann ist der Download-Button versteckt und stattdessen ein Häkchen sichtbar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFraction))]
    private double? _fraction;

    [ObservableProperty] private string _statusText = "";

    public bool CanDownload => !IsDownloading && !IsInstalled;
    public bool HasProgress => IsDownloading;
    public bool HasFraction => Fraction is not null;

    public OllamaModelRowViewModel(
        OllamaCuratedModel curated,
        Func<OllamaProvider?> providerFactory,
        Action<string>? onInstalled = null)
    {
        _providerFactory = providerFactory;
        _onInstalled = onInstalled;
        ModelName = curated.Name;
        ApproxSize = curated.ApproxSize;
        Description = curated.Description;
        VramRangeLabel = curated.MinVramGb <= 0
            ? "CPU-fähig"
            : curated.MaxVramGb >= 999
                ? $"ab {curated.MinVramGb:0.#} GB VRAM"
                : $"{curated.MinVramGb:0.#}–{curated.MaxVramGb:0.#} GB VRAM";
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        var provider = _providerFactory();
        if (provider is null)
        {
            StatusText = "✗ Ollama-Provider nicht konfiguriert.";
            return;
        }

        IsDownloading = true;
        Fraction = null;
        StatusText = "Starte Download …";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            await foreach (var ev in provider.PullAsync(ModelName, _cts.Token))
            {
                if (ev.IsError)
                {
                    StatusText = $"✗ {ev.ErrorMessage}";
                    IsDownloading = false;
                    return;
                }
                if (ev.Total is > 0 && ev.Completed is >= 0)
                    Fraction = Math.Min(1.0, ev.Completed.Value / (double)ev.Total.Value);
                else
                    Fraction = null;
                StatusText = ev.Status;
            }
            IsInstalled = true;
            IsDownloading = false;
            Fraction = 1.0;
            StatusText = "✓ Installiert.";
            _onInstalled?.Invoke(ModelName);
        }
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            StatusText = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Ollama-Pull für {Model} warf", ModelName);
            IsDownloading = false;
            StatusText = $"✗ {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }
}
