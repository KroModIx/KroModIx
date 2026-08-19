using System;
using System.IO;
using System.Text.Json;
using KroModIx.Services.Storage;
using NLog;

namespace KroModIx.Services;

/// <summary>
/// Lädt und speichert <see cref="AppSettings"/> unter dem plattformkonformen
/// Konfigurationspfad. Atomar via <see cref="JsonFileStore"/> — Quarantaene
/// NUR bei <see cref="JsonException"/>, IO-Fehler durchreichen (verhindert
/// Datenverlust bei kurzem NAS/AV-Aussetzer).
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
            JsonFileStore.WriteAtomic(_configPath, JsonSerializer.Serialize(_current, JsonOpts));
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
        string json;
        try { json = File.ReadAllText(_configPath); }
        catch (Exception ex)
        {
            // IO-Fehler → NICHT quarantaenisieren (Inhalt ist ok, nur gerade
            // nicht lesbar — beim naechsten Save wuerden wir die intakten
            // Daten sonst mit Defaults ueberschreiben, ohne Backup).
            Log.Warn(ex, "Settings-Datei nicht lesbar (temporaer?) — nutze Defaults, kein Save-Overwrite");
            return new AppSettings();
        }
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Settings-JSON defekt: {Path}", _configPath);
            JsonFileStore.Quarantine(_configPath);
            return new AppSettings();
        }
    }
}
