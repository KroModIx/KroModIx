using Avalonia.Input;
using Avalonia.Interactivity;
using KroModIx.ViewModels;

namespace KroModIx.Views;

// KEIN manuelles InitializeComponent — der Name-Generator emittiert es
// (siehe Kroste-Standards / references/avalonia12.md).
public partial class MainWindow : ChromeWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Doppelklick auf eine Sidebar-Kachel startet das Spiel.
    /// Command-Aufruf ist idempotent — wenn kein Spiel selektiert ist,
    /// tut LaunchSelectedGame nichts.</summary>
    private void OnGameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.CanLaunchSelected)
        {
            vm.LaunchSelectedGameCommand.Execute(null);
            e.Handled = true;
        }
    }
}
