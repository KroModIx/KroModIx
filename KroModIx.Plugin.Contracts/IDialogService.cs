using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Modal-Dialoge für Plugins. Abstrahiert die konkreten Avalonia-
/// Fenster weg, damit Plugins nicht an Host-View-Typen koppeln.</summary>
public interface IDialogService
{
    /// <summary>OK/Abbrechen-Frage. Rückgabe <c>true</c> = User hat bestätigt.</summary>
    Task<bool> ConfirmAsync(string title, string message,
        string? okLabel = null, string? cancelLabel = null);

    /// <summary>Reine Info/Fehler-Meldung mit OK-Button.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Datei-Auswahl. <paramref name="filters"/> im Format
    /// <c>("Label", new[]{"*.zip","*.pak"})</c>.</summary>
    Task<string?> PickFileAsync(string title, params (string Label, string[] Patterns)[] filters);

    /// <summary>Ordner-Auswahl.</summary>
    Task<string?> PickFolderAsync(string title);
}
