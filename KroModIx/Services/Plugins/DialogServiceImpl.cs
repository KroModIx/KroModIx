using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Avalonia-Impl der Plugin-Dialoge. Alle Aufrufe werden auf den UI-Thread
/// dispatcht — Plugins dürfen sie aus jedem Thread rufen.
/// </summary>
public sealed class DialogServiceImpl : IDialogService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static Window? MainWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<bool> ConfirmAsync(string title, string message,
        string? okLabel = null, string? cancelLabel = null)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = MainWindow;
            if (owner is null) return false;

            var okText = okLabel ?? "OK";
            var cancelText = cancelLabel ?? "Abbrechen";

            var okBtn = new Button { Content = okText, Classes = { "accent" }, MinWidth = 90 };
            var cancelBtn = new Button { Content = cancelText, MinWidth = 90 };

            var tcs = new TaskCompletionSource<bool>();
            var dlg = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = Avalonia.Application.Current!.Resources["KrosteBackgroundBrush"] as Avalonia.Media.IBrush,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(24),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancelBtn, okBtn },
                        },
                    },
                },
            };

            okBtn.Click += (_, _) => { tcs.TrySetResult(true); dlg.Close(); };
            cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dlg.Close(); };
            dlg.Closed += (_, _) => tcs.TrySetResult(false);

            await dlg.ShowDialog(owner);
            return await tcs.Task;
        });
    }

    public async Task ShowMessageAsync(string title, string message)
        => await ConfirmAsync(title, message, okLabel: "OK", cancelLabel: null);

    public Task<string?> PickFileAsync(string title, params (string Label, string[] Patterns)[] filters)
        => PickFileInAsync(title, null, filters);

    public async Task<string?> PickFileInAsync(string title, string? startDirectory,
        params (string Label, string[] Patterns)[] filters)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = MainWindow;
            if (owner is null) return null;
            var opts = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters.Length == 0
                    ? null
                    : filters.Select(f => new FilePickerFileType(f.Label) { Patterns = f.Patterns }).ToArray(),
            };
            // Startverzeichnis ist ein Wunsch, kein Muss: existiert der Pfad
            // nicht mehr oder kann die Plattform ihn nicht aufloesen (Portal
            // ohne Zugriff, Netzlaufwerk weg), oeffnet der Dialog eben dort,
            // wo er sonst aufgegangen waere. Ein Fehler waere hier absurd —
            // der User will eine Datei waehlen, nicht einen Pfad debuggen.
            if (!string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory))
            {
                try { opts.SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(startDirectory); }
                catch (Exception ex) { Log.Debug(ex, "Startverzeichnis {Dir} nicht aufloesbar", startDirectory); }
            }
            var files = await owner.StorageProvider.OpenFilePickerAsync(opts);
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        });
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = MainWindow;
            if (owner is null) return null;
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        });
    }
}
