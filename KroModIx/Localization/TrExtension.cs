using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace KroModIx.Localization;

/// <summary>Markup-Extension für lokalisierte Strings im XAML: <c>Text="{loc:Tr App_Title}"</c>.</summary>
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding(nameof(LocalizedString.Value))
        {
            Source = LocalizedString.Get(Key),
            Mode = BindingMode.OneWay,
        };
    }
}
