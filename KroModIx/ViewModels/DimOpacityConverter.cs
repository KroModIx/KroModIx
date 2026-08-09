using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KroModIx.ViewModels;

/// <summary>true → 0.35, false → 1.0. Wird für die „Ausgegraut"-Optik der
/// Sidebar-Kacheln von Spielen ohne Plugin genutzt, wenn der Filter
/// „Alle Spiele" aktiv ist. Alternativer Weg wäre ein Grayscale-Shader —
/// Opacity ist billiger und sieht in unserem dunklen Theme genauso aus.</summary>
public sealed class DimOpacityConverter : IValueConverter
{
    public static readonly DimOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.35 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
