using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KroModIx.Services;
using KroModIx.Services.Storage;
using NLog;

namespace KroModIx.Services.Games;

/// <summary>
/// Persistiert das Ergebnis der letzten <see cref="GameDiscoveryService.Discover"/>-
/// Runde als JSON im User-Cache. Beim App-Start wird der Cache sofort gelesen
/// und die Sidebar damit gefüllt (instant) — die eigentliche Discovery läuft
/// dann im Hintergrund und diff't die Ergebnisse zurück in die UI.
///
/// <para>Kein sicherheitskritischer Speicher — bei fehlender/kaputter Datei
/// starten wir mit leerer Liste und Discovery füllt sie neu.</para>
/// </summary>
public sealed class GamesCacheService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _cacheFile;

    public GamesCacheService()
    {
        _cacheFile = Path.Combine(AppPaths.CacheRoot, "games-cache.json");
    }

    public IReadOnlyList<DiscoveredGame> Load()
    {
        if (!File.Exists(_cacheFile)) return Array.Empty<DiscoveredGame>();
        string json;
        try { json = File.ReadAllText(_cacheFile); }
        catch (Exception ex)
        {
            // IO-Fehler → leerer Fallback. Kein Overwrite beim naechsten
            // Save (Cache wird durch Discovery eh neu befuellt — kein
            // Risiko).
            Log.Warn(ex, "Games-Cache nicht lesbar (temporaer?) — leerer Fallback");
            return Array.Empty<DiscoveredGame>();
        }
        try
        {
            var games = JsonSerializer.Deserialize<List<DiscoveredGame>>(json, JsonOptions);
            return games ?? new List<DiscoveredGame>();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Games-Cache-JSON defekt: {Path}", _cacheFile);
            JsonFileStore.Quarantine(_cacheFile);
            return Array.Empty<DiscoveredGame>();
        }
    }

    public void Save(IReadOnlyList<DiscoveredGame> games)
    {
        try
        {
            JsonFileStore.WriteAtomic(_cacheFile, JsonSerializer.Serialize(games, JsonOptions));
            Log.Debug("Games-Cache gespeichert: {N} Einträge", games.Count);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Games-Cache-Save fehlgeschlagen");
        }
    }
}
