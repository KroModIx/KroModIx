using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;

namespace ModManager.Services.Games;

/// <summary>
/// Persistiert manuell hinzugefügte Spiele. JSON in <c>~/.config/ModManager/manual-games.json</c>,
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
