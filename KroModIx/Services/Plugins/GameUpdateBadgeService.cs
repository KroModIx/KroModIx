using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Fragt zyklisch alle geladenen Plugins, die <see cref="IUpdateNotifier"/>
/// implementieren, nach ausstehenden Mod-Updates ab und cached das Ergebnis
/// pro Steam-AppId. Feuert <see cref="Changed"/> immer wenn sich die
/// aggregierte Karte ändert — das MainWindowViewModel aktualisiert damit
/// die grünen ↑-Badges auf den Sidebar-Kacheln.
///
/// <para>Refresh-Rhythmus: initial 10s nach Plugin-Activation (damit die
/// erste Runde nicht mit den Discovery-Ladezeiten kollidiert), danach alle
/// 30 Minuten. Plus manueller Trigger via <see cref="RefreshAsync"/> (z.B.
/// nach Sidebar-Refresh-Klick oder Plugin-Install).</para>
///
/// <para>Fehler einzelner Plugins blocken nichts — sie landen im Log,
/// die anderen Plugins liefern weiter.</para>
/// </summary>
public sealed class GameUpdateBadgeService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PluginActivator _activator;
    private readonly ConcurrentDictionary<int, GameUpdateInfo> _pending = new();
    private CancellationTokenSource? _loopCts;

    public GameUpdateBadgeService(PluginActivator activator)
    {
        _activator = activator;
    }

    /// <summary>Wird gefeuert, wenn sich die pro-AppId-Map ändert. Die Handler
    /// müssen auf den UI-Thread marshalen — der Service feuert auf beliebigem
    /// Worker-Thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Snapshot der aktuell bekannten Updates. Lookup by SteamAppId.
    /// Enthält nur AppIds mit <c>PendingCount &gt; 0</c>.</summary>
    public IReadOnlyDictionary<int, GameUpdateInfo> Pending => _pending;

    /// <summary>Startet die periodische Abfrage im Hintergrund. Sollte einmal
    /// nach dem MainWindow-Init aufgerufen werden.</summary>
    public void Start()
    {
        if (_loopCts is not null) return;
        _loopCts = new CancellationTokenSource();
        var ct = _loopCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { return; }
            while (!ct.IsCancellationRequested)
            {
                try { await RefreshAsync(ct); }
                catch (Exception ex) { Log.Debug(ex, "Update-Badge-Refresh-Loop warf"); }
                try { await Task.Delay(TimeSpan.FromMinutes(30), ct); } catch { return; }
            }
        }, ct);
    }

    /// <summary>Löst einen sofortigen Refresh aus. Läuft parallel zu jedem
    /// laufenden Loop-Tick, ist aber idempotent — die Merge-Logik unten
    /// gewinnt die spätere Antwort.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var loaded = _activator.Loaded
            .Where(l => l.Plugin is IUpdateNotifier)
            .ToList();
        if (loaded.Count == 0) return;

        var fresh = new Dictionary<int, GameUpdateInfo>();
        foreach (var l in loaded)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var notifier = (IUpdateNotifier)l.Plugin;
            try
            {
                var updates = await notifier.GetPendingUpdatesAsync(cancellationToken)
                    ?? Array.Empty<GameUpdateInfo>();
                foreach (var u in updates.Where(u => u.PendingCount > 0))
                {
                    // Bei mehreren Plugins für dieselbe AppId summieren wir
                    // die Zähler — theoretisch könnten LS25 + ein hypothetisches
                    // FS-Extra-Plugin beide Updates melden.
                    if (fresh.TryGetValue(u.SteamAppId, out var existing))
                        fresh[u.SteamAppId] = new GameUpdateInfo(
                            u.SteamAppId,
                            existing.PendingCount + u.PendingCount,
                            existing.Summary ?? u.Summary);
                    else
                        fresh[u.SteamAppId] = u;
                }
                Log.Debug("Update-Notifier {Plugin} lieferte {N} Updates",
                    l.Manifest.Id, updates.Count);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Update-Notifier {Plugin} warf beim GetPendingUpdatesAsync",
                    l.Manifest.Id);
            }
        }

        // Änderungs-Detection: wenn beide Maps semantisch gleich sind, nichts
        // feuern — sonst würde die Sidebar bei jedem 30-Minuten-Tick redundant
        // neu rendern.
        if (!MapsEqual(_pending, fresh))
        {
            _pending.Clear();
            foreach (var kv in fresh) _pending[kv.Key] = kv.Value;
            // Und die Einträge die nicht mehr gemeldet werden — auch weg
            foreach (var appId in _pending.Keys.ToList())
                if (!fresh.ContainsKey(appId)) _pending.TryRemove(appId, out _);

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool MapsEqual(
        IReadOnlyDictionary<int, GameUpdateInfo> a,
        IReadOnlyDictionary<int, GameUpdateInfo> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, val) in a)
        {
            if (!b.TryGetValue(key, out var other)) return false;
            if (other.PendingCount != val.PendingCount) return false;
        }
        return true;
    }

    /// <summary>Für manuelles Setzen eines Badge-Werts von außen — vom
    /// <c>/events/badge</c>-Endpoint für Screenshot-driven Iteration genutzt,
    /// solange noch kein Plugin <see cref="IUpdateNotifier"/> implementiert.
    /// Setzt <paramref name="count"/> = 0 → Badge weg.</summary>
    public void PublishForTesting(int steamAppId, int count, string? summary = null)
    {
        var changed = false;
        if (count <= 0)
        {
            if (_pending.TryRemove(steamAppId, out _)) changed = true;
        }
        else
        {
            var updated = new GameUpdateInfo(steamAppId, count, summary);
            _pending.AddOrUpdate(steamAppId, updated, (_, _) => updated);
            changed = true;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }
}
