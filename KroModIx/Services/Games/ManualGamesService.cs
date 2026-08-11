using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;

namespace KroModIx.Services.Games;

/// <summary>
/// Persistiert manuell hinzugefügte Spiele. JSON in <c>~/.config/KroModIx/manual-games.json</c>,
/// atomar via tmp+move, defekte Dateien werden als <c>.broken</c> gesichert.
/// </summary>
public sealed class ManualGamesService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private List<ManualGameEntry> _games;

    public ManualGamesService()
    {
        _path = Path.Combine(AppPaths.ConfigRoot, "manual-games.json");
        _games = Load();
    }

    public IReadOnlyList<ManualGameEntry> All => _games;

    /// <summary>Legt für ein Plugin-Manifest mit <see cref="KroModIx.Plugin.Contracts.PluginVirtualGame"/>
    /// einen Manual-Game-Anker an, falls noch keiner mit dieser <paramref name="steamAppId"/>
    /// existiert. Rückgabe true = neu angelegt, false = existierte schon.</summary>
    public bool EnsureVirtualAnchor(string displayName, int steamAppId)
    {
        if (_games.Any(g => g.SteamAppId == steamAppId))
        {
            Log.Debug("Virtual-Anchor {AppId} existiert bereits — kein Ensure nötig", steamAppId);
            return false;
        }
        // InstallDir muss syntaktisch gesetzt sein, wird aber vom Anchor selbst
        // nicht benutzt (Ren'Py-Root steht im Plugin-eigenen Settings-Store).
        var placeholder = OperatingSystem.IsWindows() ? @"C:\" : "/";
        _games.Add(new ManualGameEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            InstallDir = placeholder,
            ExecutablePath = null,
            CoverPath = null,
            SteamAppId = steamAppId,
        });
        Save();
        Log.Info("Virtual-Anchor angelegt: {Name} (AppId {AppId})", displayName, steamAppId);
        return true;
    }

    /// <summary>Fügt einen neuen Eintrag hinzu (generiert die ID) und persistiert sofort.</summary>
    public ManualGameEntry Add(string displayName, string installDir,
        string? executablePath = null, string? coverPath = null, int? steamAppId = null)
    {
        var entry = new ManualGameEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            InstallDir = installDir,
            ExecutablePath = executablePath,
            CoverPath = coverPath,
            SteamAppId = steamAppId,
        };
        _games.Add(entry);
        Save();
        Log.Info("Manuelles Spiel hinzugefügt: {Name} ({Id})", entry.DisplayName, entry.Id);
        return entry;
    }

    public bool Remove(string id)
    {
        int removed = _games.RemoveAll(g => g.Id == id);
        if (removed > 0) { Save(); Log.Info("Manuelles Spiel entfernt: {Id}", id); }
        return removed > 0;
    }

    public void Update(string id, Action<ManualGameEntry> mutate)
    {
        var entry = _games.FirstOrDefault(g => g.Id == id);
        if (entry is null) return;
        mutate(entry);
        Save();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_games, JsonOpts);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "manual-games.json konnte nicht gespeichert werden");
        }
    }

    private List<ManualGameEntry> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<ManualGameEntry>>(json, JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            var broken = _path + ".broken";
            try
            {
                if (File.Exists(broken)) File.Delete(broken);
                File.Move(_path, broken);
                Log.Error(ex, "manual-games.json defekt — als .broken gesichert: {Path}", broken);
            }
            catch (Exception moveEx)
            {
                Log.Warn(moveEx, "Konnte defekte manual-games.json nicht als .broken sichern");
            }
            return new();
        }
    }
}
