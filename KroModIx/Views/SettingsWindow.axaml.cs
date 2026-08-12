using KroModIx.ViewModels;

namespace KroModIx.Views;

public partial class SettingsWindow : ChromeWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
        // v1.14: Nexus-Key-State beim Fenster-Open laden. DataContext ist
        // beim Opened-Event garantiert gesetzt (Show() erfolgt nach der
        // Composition durch den Bootstrap).
        Opened += (_, _) =>
        {
            if (DataContext is SettingsWindowViewModel vm)
                vm.LoadNexusState();
        };
    }
}
