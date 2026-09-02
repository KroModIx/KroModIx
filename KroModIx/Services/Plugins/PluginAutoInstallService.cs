using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Services.Games;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>v1.28.1: zieht fehlende Plugins fuer bereits erkannte Spiele
/// automatisch nach — der Neuinstallations-Fall.
///
/// <para>Nach einem Neuaufsetzen ist <c>~/.config/KroModIx/plugins/</c> leer,
/// waehrend Steam-Bibliothek und <c>manual-games.json</c> weiterleben. Vorher
/// musste der User jede Sidebar-Kachel einzeln anklicken und in der
/// Install-Karte „⬇ Installieren" druecken; fuer Ordner-Sammlungen (Ren'Py)
/// gab es diese Karte nicht einmal. Jetzt laeuft der Download beim Start
/// einmal durch und aktiviert die Plugins live, ohne Neustart.</para>
///
/// <para>Der Lauf ist bewusst konservativ: nur Plugins zu vorhandenen Spielen,
/// nie ein vom User deinstalliertes Plugin, und pro Session hoechstens ein
/// Versuch je Plugin — ein Rechner ohne Netz erzeugt also keine
/// GitHub-Anfrage-Schleife.</para></summary>
public sealed class PluginAutoInstallService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsService _settings;
    private readonly PluginRegistryScanner _scanner;
    private readonly PluginInstaller _installer;
    private readonly PluginActivationPlanner _planner;
    private readonly PluginActivator _activator;
    private readonly HostUpdateService _hostUpdate;

    /// <summary>Wie lange ein gescheiterter Versuch gesperrt bleibt. Deckt den
    /// realistischen Fehlerfall ab (GitHub-Rate-Limit 403, kurzer Netz-Aussetzer)
    /// ohne bei jedem Discovery-Refresh neu zu pochen.</summary>
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(15);

    /// <summary>Plugin-ID → Zeitpunkt, ab dem ein neuer Versuch erlaubt ist.
    /// Erfolgreiche Installationen fliegen raus; die blockt danach ohnehin die
    /// „ist installiert"-Pruefung.</summary>
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Serialisiert parallele Aufrufe (Index-Load und Ordner-Import
    /// koennen dicht hintereinander feuern) — sonst planen beide dasselbe
    /// Plugin, bevor der erste Lauf es als „attempted" markiert hat.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PluginAutoInstallService(
        AppSettingsService settings,
        PluginRegistryScanner scanner,
        PluginInstaller installer,
        PluginActivationPlanner planner,
        PluginActivator activator,
        HostUpdateService hostUpdate)
    {
        _settings = settings;
        _scanner = scanner;
        _installer = installer;
        _planner = planner;
        _activator = activator;
        _hostUpdate = hostUpdate;
    }

    /// <summary>Ergebnis eines Laufs. <paramref name="Installed"/> traegt die
    /// Anzeigenamen der frisch aktivierten Plugins (fuer den Toast),
    /// <paramref name="Failed"/> die gescheiterten mit Grund (fuers Log).</summary>
    public sealed record Summary(
        IReadOnlyList<string> Installed,
        IReadOnlyList<(string Id, string Reason)> Failed)
    {
        public static readonly Summary Empty =
            new(Array.Empty<string>(), Array.Empty<(string, string)>());
        public bool AnyInstalled => Installed.Count > 0;
    }

    /// <summary>Plant und installiert. Muss auf dem UI-Thread aufgerufen werden:
    /// die Plugin-Aktivierung laeuft ueber <see cref="PluginActivator"/>, der
    /// wie bei der Install-Karte im UI-Kontext fortsetzt. Der Download selbst
    /// ist async IO und blockiert die UI nicht.</summary>
    public async Task<Summary> RunAsync(
        PluginIndex? index, IReadOnlyList<DiscoveredGame> games, CancellationToken ct = default)
    {
        if (!_settings.Current.PluginAutoInstallForMatchedGames)
        {
            Log.Debug("Auto-Install deaktiviert (PluginAutoInstallForMatchedGames=false)");
            return Summary.Empty;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var installedIds = _scanner.Scan().Select(p => p.Manifest.Id).ToList();
            var now = DateTimeOffset.UtcNow;
            var cooling = _cooldownUntil
                .Where(kv => kv.Value > now)
                .Select(kv => kv.Key)
                .ToList();
            var plan = PluginAutoInstallPlanner.Plan(
                index, games, installedIds,
                _settings.Current.AutoInstallOptOutPluginIds,
                cooling);

            if (plan.Count == 0) return Summary.Empty;

            Log.Info("Auto-Install: {N} fehlende(s) Plugin(s) fuer erkannte Spiele: {Ids}",
                plan.Count, string.Join(", ", plan.Select(p => p.Id)));

            var installed = new List<string>();
            var failed = new List<(string, string)>();
            var hostVer = ParseHostVersion(_hostUpdate.CurrentVersion);

            foreach (var entry in plan)
            {
                ct.ThrowIfCancellationRequested();
                // VOR dem Versuch sperren — ein Abbruch mitten im Download darf
                // beim naechsten Refresh nicht sofort neu anlaufen.
                _cooldownUntil[entry.Id] = DateTimeOffset.UtcNow + RetryCooldown;
                var error = await InstallAndActivateAsync(entry, games, hostVer, ct).ConfigureAwait(true);
                if (error is null)
                {
                    _cooldownUntil.Remove(entry.Id);
                    installed.Add(entry.DisplayName);
                }
                else failed.Add((entry.Id, error));
            }

            foreach (var (id, reason) in failed)
                Log.Warn("Auto-Install {Id} fehlgeschlagen: {Reason}", id, reason);
            if (installed.Count > 0)
                Log.Info("Auto-Install: {N} Plugin(s) aktiv: {Names}",
                    installed.Count, string.Join(", ", installed));

            return new Summary(installed, failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Download + Live-Aktivierung eines Eintrags. Rueckgabe null =
    /// erfolgreich, sonst der Fehlergrund.</summary>
    private async Task<string?> InstallAndActivateAsync(
        PluginIndexEntry entry, IReadOnlyList<DiscoveredGame> games,
        Version hostVersion, CancellationToken ct)
    {
        try
        {
            var result = await _installer.InstallLatestAsync(entry, ct).ConfigureAwait(true);
            if (!result.Success || result.Plugin is null)
                return result.ErrorMessage ?? "Download fehlgeschlagen";

            var decision = _planner.PlanSingle(result.Plugin, games, hostVersion);
            if (!decision.Activate)
                return $"heruntergeladen, aber nicht aktivierbar ({decision.SkipReason})";

            var loaded = await _activator.ActivateOneAsync(decision).ConfigureAwait(true);
            return loaded is null ? "Aktivierung fehlgeschlagen" : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Merkt sich, dass der User dieses Plugin bewusst deinstalliert
    /// hat — der naechste Start zieht es dann nicht wieder nach.</summary>
    public void OptOut(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        _settings.Update(s =>
        {
            if (!s.AutoInstallOptOutPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
                s.AutoInstallOptOutPluginIds.Add(pluginId);
        });
        _cooldownUntil.Remove(pluginId);
        Log.Info("Auto-Install-Opt-out fuer {Id} gesetzt (vom User deinstalliert)", pluginId);
    }

    /// <summary>Gegenstueck zu <see cref="OptOut"/>: der User hat das Plugin
    /// ueber die Install-Karte selbst wieder geholt, also darf der Auto-Install
    /// es kuenftig auch wieder nachziehen.</summary>
    public void ClearOptOut(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        if (!_settings.Current.AutoInstallOptOutPluginIds
                .Contains(pluginId, StringComparer.OrdinalIgnoreCase)) return;
        _settings.Update(s => s.AutoInstallOptOutPluginIds
            .RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)));
        _cooldownUntil.Remove(pluginId);
        Log.Info("Auto-Install-Opt-out fuer {Id} aufgehoben (manuell installiert)", pluginId);
    }

    private static Version ParseHostVersion(string s)
    {
        int dash = s.IndexOf('-'); if (dash >= 0) s = s[..dash];
        int plus = s.IndexOf('+'); if (plus >= 0) s = s[..plus];
        return Version.TryParse(s, out var v) ? v : new Version(0, 0);
    }
}
