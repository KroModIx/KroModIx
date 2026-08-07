using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;

namespace ModManager.Localization;

/// <summary>
/// Singleton-Service für UI-Lokalisierung. Direkter <see cref="ResourceManager"/>-Zugriff
/// (kein Designer-Wrapper — der ResX-Generator läuft nur unter Visual Studio zuverlässig).
/// Sprachwechsel wirkt live via <see cref="LocalizedString.NotifyAllChanged"/>.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public static IReadOnlyList<(string Iso, string Display, string Flag)> SupportedCultures { get; } = new[]
    {
        ("en", "English", "\U0001F1EC\U0001F1E7"),
        ("de", "Deutsch", "\U0001F1E9\U0001F1EA"),
    };

    private readonly ResourceManager _rm = new(
        "ModManager.Localization.Strings",
        typeof(LocalizationService).Assembly);

    private CultureInfo _current = CultureInfo.CurrentUICulture;

    public CultureInfo Current
    {
        get => _current;
        set
        {
            if (Equals(_current, value)) return;
            _current = value;
            CultureInfo.CurrentUICulture = value;
            LocalizedString.NotifyAllChanged();
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(CurrentIso));
        }
    }

    public string CurrentIso => TwoLetterOrDefault(_current);

    public void SetCulture(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            Current = CultureInfo.InvariantCulture;
            return;
        }
        Current = SupportedCultures.Any(c => c.Iso == iso)
            ? CultureInfo.GetCultureInfo(iso)
            : CultureInfo.InvariantCulture;
    }

    public string this[string key]
    {
        get
        {
            try
            {
                return _rm.GetString(key, _current) ?? $"!{key}!";
            }
            catch (MissingManifestResourceException)
            {
                return $"!{key}!";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string TwoLetterOrDefault(CultureInfo c)
    {
        var iso = c.TwoLetterISOLanguageName;
        return SupportedCultures.Any(x => x.Iso == iso) ? iso : "en";
    }
}
