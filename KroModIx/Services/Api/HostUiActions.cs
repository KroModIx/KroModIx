using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KroModIx.ViewModels;
using KroModIx.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace KroModIx.Services.Api;

/// <summary>
/// Alle Zugriffe des ApiHost auf den Avalonia-Visual-Tree laufen hier durch.
/// Jede Methode marshalt via <see cref="Dispatcher.UIThread"/> und liefert
/// erst zurück wenn die UI-Aktion abgeschlossen ist — dann kann der
/// HTTP-Handler synchron eine Response bauen.
///
/// <para>Bewusst kein DI-Konstruktor — die Klasse hält keinen State und wird
/// direkt aus ApiEndpoints instanziiert (mit dem <see cref="IServiceProvider"/>
/// als einzigem Fenster in die App).</para>
/// </summary>
internal sealed class HostUiActions
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _services;

    public HostUiActions(IServiceProvider services) => _services = services;

    public Task<Window?> GetMainWindowAsync() =>
        Dispatcher.UIThread.InvokeAsync(() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow).GetTask();

    public Task<Window?> GetActiveWindowAsync() =>
        Dispatcher.UIThread.InvokeAsync<Window?>(() =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime is null) return null;
            // FocusedWindow first — Settings-Dialog etc. Falls nichts fokussiert
            // (App im Hintergrund), MainWindow als Fallback.
            var focused = lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow;
            return focused;
        }).GetTask();

    public Task<MainWindowViewModel?> GetMainVmAsync() =>
        Dispatcher.UIThread.InvokeAsync<MainWindowViewModel?>(() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .MainWindow?.DataContext as MainWindowViewModel).GetTask();

    /// <summary>Öffnet ein Sekundär-Fenster über den entsprechenden Command im MainVM
    /// (Settings/About/PluginManager/PluginUpdates). Wirft <see cref="ArgumentException"/>
    /// bei unbekanntem Key.</summary>
    public Task OpenWindowAsync(string window) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var vm = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .MainWindow?.DataContext as MainWindowViewModel;
            if (vm is null) throw new InvalidOperationException("MainWindow-VM nicht verfügbar.");

            switch (window)
            {
                case "settings": vm.OpenSettingsCommand.Execute(null); break;
                case "about": vm.OpenAboutCommand.Execute(null); break;
                case "pluginManager":
                case "pluginUpdates":
                    vm.OpenPluginUpdatesCommand.Execute(null); break;
                default:
                    throw new ArgumentException($"Unbekannter window-Key '{window}'. Erlaubt: settings|about|pluginManager|pluginUpdates.");
            }
        }).GetTask();

    /// <summary>Klick simulieren: sucht das Named-Element und feuert je nach
    /// Control-Typ die passende Aktion. Für Standard-<see cref="Button"/> ruft
    /// <c>Command</c> mit <c>CommandParameter</c> — das ist der zuverlässigste
    /// Weg, weil manche Handler nur an Command hängen und nicht am Click-Event.</summary>
    public Task<ClickResult> ClickAsync(string elementId, int clickCount) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (control, avail) = FindNamed(elementId);
            if (control is null) return ClickResult.NotFound(avail);

            // WICHTIG: In Avalonia erbt CheckBox : ToggleButton : Button. Spezifische
            // Typen MÜSSEN vor Button stehen, sonst wären sie als unreachable
            // markiert (die Base-Klasse „schluckt" sie).
            switch (control)
            {
                case TabItem tab:
                    // Tab aktivieren = SelectedItem des umgebenden TabControl setzen.
                    // Bindung an IsSelected zieht nicht immer sauber, wenn andere Items
                    // dynamisch geändert werden — direkter Weg ist der TabControl-Parent.
                    if (tab.Parent is TabControl tc) tc.SelectedItem = tab;
                    else tab.IsSelected = true;
                    return ClickResult.Ok;
                case CheckBox cb:
                    cb.IsChecked = !(cb.IsChecked ?? false);
                    return ClickResult.Ok;
                case ToggleButton toggle:
                    toggle.IsChecked = !(toggle.IsChecked ?? false);
                    return ClickResult.Ok;
                case Button btn:
                    // Alle KroModIx-Buttons hängen an Commands (MVVM). Reines
                    // Click-Event ohne Command wäre für die API-Steuerung nicht
                    // greifbar (Interactivity.RaiseEvent via Reflection wäre
                    // hässlich). Falls das Bedarf wird, hier ergänzen.
                    if (btn.Command is null)
                    {
                        Log.Debug("Button '{Id}' ohne Command — kein sichtbarer Effekt.", elementId);
                        return ClickResult.Ok;
                    }
                    if (btn.Command.CanExecute(btn.CommandParameter))
                        btn.Command.Execute(btn.CommandParameter);
                    return ClickResult.Ok;
                case ListBox _ when clickCount >= 2:
                    // Doppelklick auf Liste = Launch. Wird von der Sidebar-
                    // ListBox erwartet (OnGameDoubleTapped).
                    var vm = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                        .MainWindow?.DataContext as MainWindowViewModel;
                    if (vm is not null && vm.CanLaunchSelected)
                        vm.LaunchSelectedGameCommand.Execute(null);
                    return ClickResult.Ok;
                default:
                    Log.Debug("Element '{Id}' ist ein {Type} — Click-Semantik unbekannt.", elementId, control.GetType().Name);
                    return ClickResult.Ok;
            }
        }).GetTask();

    /// <summary>Text in ein Input-Element schreiben. Nur <see cref="TextBox"/>
    /// wird aktuell unterstützt (das ist alles was das MainWindow hat).</summary>
    public Task<TextResult> SetTextAsync(string elementId, string text, bool selectAll) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (control, avail) = FindNamed(elementId);
            if (control is null) return TextResult.NotFound(avail);

            if (control is TextBox tb)
            {
                if (selectAll) { tb.SelectionStart = 0; tb.SelectionEnd = tb.Text?.Length ?? 0; }
                tb.Text = text;
                return TextResult.Ok;
            }
            return TextResult.Unsupported(control.GetType().Name);
        }).GetTask();

    /// <summary>PNG-Snapshot des angegebenen Fensters via
    /// <see cref="RenderTargetBitmap"/>. Rendering läuft auf dem UI-Thread —
    /// low-frequency Endpoint, off-UI-Encode lohnt nicht.</summary>
    public Task<byte[]?> ScreenshotAsync(string target) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime is null) return (byte[]?)null;

            Window? win = target == "active"
                ? (lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow)
                : lifetime.MainWindow;

            if (win is null) return null;

            var size = win.ClientSize;
            if (size.Width < 1 || size.Height < 1) return null;

            var pixel = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(size.Width)),
                Math.Max(1, (int)Math.Ceiling(size.Height)));
            var dpi = new Vector(96, 96);
            using var rtb = new RenderTargetBitmap(pixel, dpi);
            rtb.Render(win);
            using var ms = new MemoryStream();
            rtb.Save(ms, PngBitmapEncoderOptions.Default);
            return ms.ToArray();
        }).GetTask();

    /// <summary>Named-Element im Visual Tree aller offenen Fenster finden.
    /// Sucht in dieser Reihenfolge: fokussiertes Fenster → MainWindow →
    /// alle übrigen. Liefert zusätzlich die Liste aller sichtbaren Named-Elements
    /// als Debug-Hilfe für die 404-Response.</summary>
    private static (Control? Control, IReadOnlyList<string> Available) FindNamed(string elementId)
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime is null) return (null, Array.Empty<string>());

        var windows = new List<Window>();
        var focused = lifetime.Windows.FirstOrDefault(w => w.IsActive);
        if (focused is not null) windows.Add(focused);
        if (lifetime.MainWindow is not null && !windows.Contains(lifetime.MainWindow)) windows.Add(lifetime.MainWindow);
        foreach (var w in lifetime.Windows) if (!windows.Contains(w)) windows.Add(w);

        Control? found = null;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var win in windows)
        {
            CollectAndFind(win, elementId, names, ref found);
        }
        return (found, names.ToList());
    }

    private static void CollectAndFind(Visual root, string elementId, SortedSet<string> names, ref Control? found)
    {
        foreach (var d in root.GetVisualDescendants())
        {
            if (d is Control c && !string.IsNullOrEmpty(c.Name))
            {
                names.Add(c.Name);
                if (found is null && c.Name == elementId) found = c;
            }
        }
    }

    public sealed record ClickResult(bool Success, string? Error, IReadOnlyList<string> Available)
    {
        public static readonly ClickResult Ok = new(true, null, Array.Empty<string>());
        public static ClickResult NotFound(IReadOnlyList<string> avail) => new(false, "Element not found", avail);
    }

    public sealed record TextResult(bool Success, string? Error, IReadOnlyList<string> Available)
    {
        public static readonly TextResult Ok = new(true, null, Array.Empty<string>());
        public static TextResult NotFound(IReadOnlyList<string> avail) => new(false, "Element not found", avail);
        public static TextResult Unsupported(string typeName) => new(false, $"Element vom Typ {typeName} unterstützt keine Text-Eingabe.", Array.Empty<string>());
    }
}

