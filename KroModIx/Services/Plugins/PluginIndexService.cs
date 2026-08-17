using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Lädt <c>plugins.json</c> aus <c>KroModIx/KroModIx.PluginIndex</c> und
/// cacht das Ergebnis für 6 Stunden. Bei Netz-Fehler wird der Cache — auch
/// wenn abgelaufen — weiter genutzt (App bleibt bedienbar ohne Internet).
///
/// <para>v1.19.1: Cache-TTL von 24h auf 6h reduziert. Zusätzlich beim ersten
/// <see cref="GetAsync"/>-Call ein <b>Background-Refresh</b> gefeuert, der
/// den File-Cache silent aktualisiert — der User bekommt neue Plugin-
/// Eintraege spaetestens beim naechsten App-Start ohne warten zu muessen.
/// Neu released: <see cref="RefreshAsync"/> als expliziter Force-Refresh
/// (vom Plugins-Fenster-Button aufgerufen). Vorher stand der User bis zu
/// 24h vor „kein Plugin verfuegbar", obwohl das Plugin schon released war
/// (real passiert am 2026-08-17 mit Schedule I).</para>
/// </summary>
public sealed class PluginIndexService
{
    private const string IndexUrl =
        "https://raw.githubusercontent.com/KroModIx/KroModIx.PluginIndex/main/plugins.json";
    private const int CacheTtlHours = 6;

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _cachePath;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private PluginIndex? _cached;
    private bool _backgroundRefreshTriggered;

    /// <summary>Wird gefeuert wenn ein (Background- oder expliziter) Refresh
    /// neue Katalog-Daten geladen hat. UI kann sich rebinden.</summary>
    public event EventHandler? IndexRefreshed;

    public PluginIndexService()
    {
        _cachePath = Path.Combine(AppPaths.CacheRoot, "plugin-index.json");
    }

    /// <summary>Blockierender Fetch mit Cache-Fallback. Rückgabe ist niemals
    /// null — bei totalem Failure eine leere <see cref="PluginIndex"/>-Instanz.
    /// Beim ersten Call einer Session wird zusaetzlich ein Background-Refresh
    /// gestartet (fire-and-forget), der den Cache silent updated.</summary>
    public async Task<PluginIndex> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            TriggerBackgroundRefreshOnce();
            return _cached;
        }

        // (a) Aktueller Cache?
        if (TryLoadCache(out var cached, out var cacheAge)
            && cached is not null
            && cacheAge < TimeSpan.FromHours(CacheTtlHours))
        {
            _cached = cached;
            TriggerBackgroundRefreshOnce();
            return cached;
        }

        // (b) Neu laden.
        var fresh = await FetchAsync(ct).ConfigureAwait(false);
        if (fresh is not null)
        {
            _cached = fresh;
            return fresh;
        }

        // (c) Fallback: stale Cache, oder leerer Index.
        if (cached is not null)
        {
            _cached = cached;
            return cached;
        }
        _cached = new PluginIndex();
        return _cached;
    }

    /// <summary>Expliziter Force-Refresh — vom Plugins-Fenster-Button
    /// aufgerufen. Umgeht den TTL-Check und laedt frisch aus dem Repo.
    /// Rueckgabe: die neue Instanz (oder der stale Cache bei Netz-Fehler).
    /// Feuert <see cref="IndexRefreshed"/> wenn tatsaechlich neu geladen.</summary>
    public async Task<PluginIndex> RefreshAsync(CancellationToken ct = default)
    {
        var fresh = await FetchAsync(ct).ConfigureAwait(false);
        if (fresh is not null)
        {
            _cached = fresh;
            IndexRefreshed?.Invoke(this, EventArgs.Empty);
            return fresh;
        }
        return _cached ?? new PluginIndex();
    }

    private void TriggerBackgroundRefreshOnce()
    {
        if (_backgroundRefreshTriggered) return;
        _backgroundRefreshTriggered = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var fresh = await FetchAsync(CancellationToken.None).ConfigureAwait(false);
                if (fresh is not null)
                {
                    _cached = fresh;
                    IndexRefreshed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Background-Refresh Plugin-Index fehlgeschlagen");
            }
        });
    }

    private async Task<PluginIndex?> FetchAsync(CancellationToken ct)
    {
        if (!await _refreshGate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KroModIx-PluginIndex");

            var json = await http.GetStringAsync(IndexUrl, ct).ConfigureAwait(false);
            var idx = JsonSerializer.Deserialize<PluginIndex>(json, JsonOpts)
                      ?? new PluginIndex();

            try { await File.WriteAllTextAsync(_cachePath, json, ct).ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug(ex, "Konnte Plugin-Index-Cache nicht schreiben"); }

            Log.Info("Plugin-Index geladen: {N} Plugin(s) im Katalog", idx.Plugins.Count);
            return idx;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Index nicht erreichbar — nutze evtl. veralteten Cache");
            return null;
        }
        finally { _refreshGate.Release(); }
    }

    private bool TryLoadCache(out PluginIndex? index, out TimeSpan age)
    {
        index = null;
        age = TimeSpan.MaxValue;
        if (!File.Exists(_cachePath)) return false;
        try
        {
            age = DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath);
            var json = File.ReadAllText(_cachePath);
            index = JsonSerializer.Deserialize<PluginIndex>(json, JsonOpts);
            return index is not null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Plugin-Index-Cache defekt");
            return false;
        }
    }
}
