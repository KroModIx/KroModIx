using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace KroModIx.Controls;

public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public TitleBar()
    {
        InitializeComponent();
        MinButton.Click += (_, _) => { if (Host is { } w) w.WindowState = WindowState.Minimized; };
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Host?.Close();
        Bar.PointerPressed += OnBarPointerPressed;
        Bar.DoubleTapped += OnBarDoubleTapped;
    }

    // Avalonia 12: VisualRoot ist NICHT das Window — TopLevel.GetTopLevel benutzen.
    private Window? Host => TopLevel.GetTopLevel(this) as Window;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
            TitleText.Text = Title;
    }

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Pflicht-Guard (kroste-avalonia-Skill 2026-08-19): ohne den frisst
        // BeginMoveDrag jeden Klick auf ein interaktives Kind der Titelleiste
        // (ComboBox waere der klassische Fall: bekommt kein PointerReleased,
        // Dropdown oeffnet nicht). KroModIx hat aktuell nichts Interaktives
        // oben — der Guard ist praeventiv fuer den naechsten „Global-Search-
        // Bar oben"-Umbau.
        if (LandedOnInteractiveChild(e.Source))
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void OnBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LandedOnInteractiveChild(e.Source))
            return;
        ToggleMaximize();
    }

    /// <summary>Laeuft vom Event-Ursprung den Visual-Tree hoch bis zur
    /// Titelleisten-Border und meldet true, wenn unterwegs ein interaktives
    /// Control liegt. Button captured PointerPressed selbst, ComboBox tut das
    /// nicht — ohne Guard startet <c>BeginMoveDrag</c> und die Combo bekommt
    /// nie ein PointerReleased.</summary>
    private bool LandedOnInteractiveChild(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            // Titelleiste selbst (und alles darueber) ist Drag-Flaeche.
            if (ReferenceEquals(v, Bar))
                return false;

            // Button deckt ToggleButton/CheckBox/RadioButton/RepeatButton mit ab.
            if (v is Button or ComboBox or TextBox or Slider or ListBox or MenuItem)
                return true;

            // Auffangnetz: alles Fokussierbare will den Klick selbst verarbeiten.
            if (v is InputElement { Focusable: true })
                return true;
        }

        // Ursprung liegt ausserhalb der Titelleiste (z.B. in einem Popup-Root).
        return true;
    }

    private void ToggleMaximize()
    {
        if (Host is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }
}
