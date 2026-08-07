using System;
using System.IO;
using System.Text.Json;
using NLog;

namespace ModManager.Services;

/// <summary>
/// Lädt und speichert <see cref="AppSettings"/> unter dem plattformkonformen
/// Konfigurationspfad. Atomar via tmp+move. Defekte Dateien werden als
/// <c>.broken</c> gesichert statt kommentarlos überschrieben.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _configPath;
    private AppSettings _current;

    public AppSettingsService()
    {
        _configPath = Path.Combine(AppPaths.ConfigRoot, "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    public void Update(Action<AppSettings> mutate)
    {
        mutate(_current);
        Save();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_current, JsonOpts);
            var tmp = _configPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _configPath, overwrite: true);
            Log.Debug("Settings gespeichert: {Path}", _configPath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte Settings nicht speichern: {Path}", _configPath);
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_configPath))
        {
            Log.Info("Keine Settings-Datei — nutze Defaults");
            return new AppSettings();
        }
        try
        {
            var json = File.ReadAllText(_configPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            return loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            var brokenPath = _configPath + ".broken";
            try
            {
                if (File.Exists(brokenPath)) File.Delete(brokenPath);
                File.Move(_configPath, brokenPath);
                Log.Error(ex, "Settings-Datei defekt — als .broken gesichert: {Path}", brokenPath);
            }
            catch (Exception moveEx)
            {
                Log.Warn(moveEx, "Konnte defekte Settings nicht als .broken sichern");
            }
            return new AppSettings();
        }
    }
}
