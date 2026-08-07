using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace ModManager.Services.Plugins;

/// <summary>
/// Lädt <c>plugins.json</c> aus <c>Kroste/ModManager.PluginIndex</c> und
/// cacht das Ergebnis für 24 Stunden. Bei Netz-Fehler wird der Cache — auch
/// wenn abgelaufen — weiter genutzt (App bleibt bedienbar ohne Internet).
/// </summary>
public sealed class PluginIndexService
{
    private const string IndexUrl =
        "https://raw.githubusercontent.com/Kroste/ModManager.PluginIndex/main/plugins.json";
    private const int CacheTtlHours = 24;

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _cachePath;
    private PluginIndex? _cached;

    public PluginIndexService()
    {
        _cachePath = Path.Combine(AppPaths.CacheRoot, "plugin-index.json");
    }

    /// <summary>Blockierender Fetch mit Cache-Fallback. Rückgabe ist niemals
    /// null — bei totalem Failure eine leere <see cref="PluginIndex"/>-Instanz.</summary>
    public async Task<PluginIndex> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        // (a) Aktueller Cache?
        if (TryLoadCache(out var cached, out var cacheAge)
            && cached is not null
            && cacheAge < TimeSpan.FromHours(CacheTtlHours))
        {
            _cached = cached;
            return cached;
        }

        // (b) Neu laden.
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ModManager-PluginIndex");

            var json = await http.GetStringAsync(IndexUrl, ct).ConfigureAwait(false);
            var idx = JsonSerializer.Deserialize<PluginIndex>(json, JsonOpts)
                      ?? new PluginIndex();

            try { await File.WriteAllTextAsync(_cachePath, json, ct).ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug(ex, "Konnte Plugin-Index-Cache nicht schreiben"); }

            _cached = idx;
            Log.Info("Plugin-Index geladen: {N} Plugin(s) im Katalog", idx.Plugins.Count);
            return idx;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Index nicht erreichbar — nutze evtl. veralteten Cache");
            if (cached is not null)
            {
                _cached = cached;
                return cached;
            }
            _cached = new PluginIndex();
            return _cached;
        }
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
