using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ModManager.Localization;
using ModManager.Services;

namespace ModManager.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;

    public IReadOnlyList<LanguageOption> Languages { get; }

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public SettingsWindowViewModel(AppSettingsService settings)
    {
        _settings = settings;

        Languages = new List<LanguageOption>
        {
            new("", "System / Auto", ""),
        }.Concat(LocalizationService.SupportedCultures
                .Select(c => new LanguageOption(c.Iso, $"{c.Flag}  {c.Display}", c.Iso)))
            .ToList();

        var current = settings.Current.UiCulture ?? "";
        SelectedLanguage = Languages.FirstOrDefault(l => l.Iso == current) ?? Languages[0];
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;
        var iso = string.IsNullOrEmpty(value.Iso) ? null : value.Iso;
        _settings.Update(s => s.UiCulture = iso);
        LocalizationService.Instance.SetCulture(iso ?? CultureInfo.InvariantCulture.TwoLetterISOLanguageName);
    }
}

public sealed record LanguageOption(string Iso, string DisplayLabel, string ShortIso);
