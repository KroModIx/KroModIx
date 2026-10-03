using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.TestKit;

/// <summary>Eine <see cref="IHostServices"/>-Attrappe für Plugin-Tests: alle
/// zwölf Pflichtglieder harmlos belegt, die Baukästen als setzbare
/// Eigenschaften.
///
/// <para><b>Warum im TestKit.</b> Ein Plugin, das einen seiner Dienste gegen
/// den Host testen will, musste bisher alle zwölf Pflichtglieder selbst
/// nachbauen. Der Preis dafür war, dass es keines tat: der GitHub-Weg des
/// CoI-Update-Prüfers, der UE4SS-Bootstrap in Icarus, BepInEx in Dyson
/// Sphere Program und MelonLoader in Schedule I waren am 03.10.2026
/// durchweg <b>ungetestet</b>, obwohl in allen vier derselbe Fehler steckte
/// (eine fest hinterlegte Ausweich-URL, die mit jeder neuen Loader-Ausgabe
/// weiter veraltet).</para>
///
/// <para>Die Dienste, die kein Test braucht, antworten still statt zu
/// werfen — ein Test soll an seiner Behauptung scheitern, nicht daran, dass
/// das Plugin nebenbei eine Meldung anzeigen wollte. Wer eine davon prüfen
/// will, ersetzt sie: alle sind <c>virtual</c> oder setzbar.</para></summary>
public class FakeHostServices : IHostServices
{
    private readonly string _root;

    public FakeHostServices(string? rootDir = null)
    {
        _root = rootDir ?? Directory.CreateTempSubdirectory("kromodix-faker").FullName;
        PluginDataDir = Path.Combine(_root, "data");
        PluginCacheDir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(PluginDataDir);
        Directory.CreateDirectory(PluginCacheDir);
    }

    public Logger Logger { get; } = LogManager.GetLogger("FakeHost");
    public string PluginDataDir { get; }
    public string PluginCacheDir { get; }

    /// <summary>Alles, was ein Plugin über <see cref="Notifications"/>
    /// gemeldet hat — damit ein Test prüfen kann, dass ein Fehlschlag auch
    /// beim Nutzer ankommt und nicht nur im Protokoll.</summary>
    public List<(string Message, NotificationLevel Level)> Notified { get; } = [];

    /// <summary>Setzbar: hier kommt <see cref="FakeArchiveService"/> hin.</summary>
    public IArchiveService Archives { get; set; } = NullArchiveService.Instance;

    /// <summary>Setzbar: hier kommt <see cref="FakeUnrealPakService"/> hin.</summary>
    public IUnrealPakService UnrealPaks { get; set; } = NullUnrealPakService.Instance;

    /// <summary>Setzbar: hier kommt <see cref="FakeGitHubService"/> hin.</summary>
    public IGitHubService GitHub { get; set; } = NullGitHubService.Instance;

    public ISecretProtection Secrets { get; set; } = new PassthroughSecrets();
    public IDialogService Dialogs { get; set; } = new SilentDialogs();
    public ILocalization Localization { get; set; } = new GermanLocalization();
    public IHostShell Shell { get; set; } = new NoopShell();
    public IAiService Ai { get; set; } = new UnavailableAi();

    public INotificationSink Notifications => _notifications ??= new RecordingSink(this);
    private INotificationSink? _notifications;

    public virtual HttpClient CreateHttpClient(string? subsystem = null)
        => new(CreateHttpClientHandler());

    /// <summary>Ein echter Handler — ein Test, der wirklich ins Netz greift,
    /// ist selten gewollt, aber den Weg abzuschneiden wäre eine stille
    /// Verhaltensänderung. Wer das verhindern will, überschreibt die
    /// Methode.</summary>
    public virtual HttpClientHandler CreateHttpClientHandler(CookieContainer? cookies = null)
        => cookies is null ? new HttpClientHandler() : new HttpClientHandler { CookieContainer = cookies };

    public virtual IProgressScope BeginProgress(string title) => new NoopProgress();

    private sealed class RecordingSink(FakeHostServices owner) : INotificationSink
    {
        public void Notify(string message, NotificationLevel level = NotificationLevel.Info)
            => owner.Notified.Add((message, level));
    }

    /// <summary>Gibt zurück, was hereinkam. Verschlüsselung ist
    /// plattformabhängig und für die Frage, die ein Plugin-Test stellt,
    /// gleichgültig — hier zählt, dass ein gespeicherter Wert wieder
    /// herauskommt.</summary>
    private sealed class PassthroughSecrets : ISecretProtection
    {
        public string? Protect(string? plaintext) => plaintext;
        public string? Unprotect(string? ciphertext) => ciphertext;
    }

    /// <summary>Lehnt jede Rückfrage ab. Das ist die ungefährliche
    /// Richtung: ein Test, der versehentlich in einen Bestätigungsdialog
    /// läuft, bricht dort ab, statt eine Löschung durchzuwinken.</summary>
    private sealed class SilentDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message,
            string? okLabel = null, string? cancelLabel = null) => Task.FromResult(false);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<string?> PickFileAsync(string title,
            params (string Label, string[] Patterns)[] filters) => Task.FromResult<string?>(null);
        public Task<string?> PickFileInAsync(string title, string? startDir,
            params (string Label, string[] Patterns)[] filters) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class GermanLocalization : ILocalization
    {
        public string CurrentIso => "de";
        public event EventHandler? CurrentChanged;
        // Nie gefeuert; das Ereignis existiert, weil die Schnittstelle es
        // verlangt. Der Aufruf haelt den Compiler ruhig, ohne etwas zu tun.
        internal void Raise() => CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Meldet sich als nicht erreichbar. Ein Plugin, das die KI
    /// nutzt, soll im Test den Weg gehen, den es auch beim Nutzer ohne
    /// eingerichteten Anbieter geht.</summary>
    private sealed class UnavailableAi : IAiService
    {
        public string ProviderInfo => "Attrappe (nicht eingerichtet)";
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Kein KI-Anbieter in der Attrappe eingerichtet.");
    }

    private sealed class NoopShell : IHostShell
    {
        public void OpenExternalUrl(string url) { }
        public void OpenDirectory(string path) { }
        public void RequestNavigation(string gameId, string? tabId = null) { }
    }

    private sealed class NoopProgress : IProgressScope
    {
        public void Report(double fraction, string? message = null) { }
        public void SetIndeterminate(string? message = null) { }
        public void Dispose() { }
    }
}
