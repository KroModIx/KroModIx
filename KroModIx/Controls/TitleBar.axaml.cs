using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

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
        Bar.DoubleTapped += (_, _) => ToggleMaximize();
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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void ToggleMaximize()
    {
        if (Host is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }
}
