using System;
using Avalonia.Platform.Storage;
using KroModIx.ViewModels;

namespace KroModIx.Views;

public partial class AddGameDialog : ChromeWindow
{
    public AddGameDialog()
    {
        InitializeComponent();

        PickDirButton.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Verzeichnis wählen", AllowMultiple = false });
            if (folders.Count > 0 && DataContext is AddGameDialogViewModel vm)
                vm.InstallDir = folders[0].TryGetLocalPath() ?? vm.InstallDir;
        };

        PickExeButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions { Title = "Executable wählen", AllowMultiple = false });
            if (files.Count > 0 && DataContext is AddGameDialogViewModel vm)
                vm.ExecutablePath = files[0].TryGetLocalPath() ?? vm.ExecutablePath;
        };

        PickCoverButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Cover-Bild wählen",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Bilder") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png" } },
                    },
                });
            if (files.Count > 0 && DataContext is AddGameDialogViewModel vm)
                vm.CoverPath = files[0].TryGetLocalPath() ?? vm.CoverPath;
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is AddGameDialogViewModel vm)
                vm.RequestClose += (_, _) => Close();
        };
    }
}
