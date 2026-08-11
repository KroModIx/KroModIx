using Avalonia.Platform.Storage;
using KroModIx.ViewModels;

namespace KroModIx.Views;

public partial class AddFolderCollectionDialog : ChromeWindow
{
    public AddFolderCollectionDialog()
    {
        InitializeComponent();

        PickDirButton.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Root-Ordner wählen", AllowMultiple = false });
            if (folders.Count > 0 && DataContext is AddFolderCollectionDialogViewModel vm)
                vm.RootDir = folders[0].TryGetLocalPath() ?? vm.RootDir;
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is AddFolderCollectionDialogViewModel vm)
                vm.RequestClose += (_, _) => Close();
        };
    }
}
