using System;
using System.IO;
using NLog;

namespace KroModIx.Services.Storage;

/// <summary>
/// Gemeinsame Datei-Primitiven fuer alle JSON-Stores der App (Settings,
/// Manual-Games, Games-Cache, Host-Profile, Plugin-Update-Cache …).
///
/// Zwei Regeln (portiert aus kroste-avalonia-Skill v2026-08-19 —
/// realer Totalverlust-Vorfall bei DTM):
///
/// 1. <b>Atomar schreiben.</b> Ein <c>File.WriteAllText</c> direkt auf die
///    Zieldatei laesst bei Absturz/Stromausfall mitten im Schreiben eine
///    halbe Datei zurueck. Stattdessen erst nach <c>&lt;datei&gt;.tmp</c>,
///    dann <c>File.Move(tmp, ziel, overwrite: true)</c> — das Move ist
///    auf modernen Filesystemen atomar.
///
/// 2. <b>Defekte Daten nicht stillschweigend verlieren.</b> Laesst sich
///    die Datei nicht deserialisieren, wird sie NACH
///    <c>&lt;datei&gt;.broken</c> verschoben statt beim naechsten Save
///    ueberschrieben. Der User kann sie fuer Diagnose/Rettung
///    inspizieren.
///
/// <para>Bewusst NICHT quarantaenisiert wird bei IO-Fehlern (Datei
/// gesperrt, NAS kurz weg, Windows-AV-Scanner) — dort ist der Inhalt
/// ja in Ordnung, nur gerade nicht lesbar. Ein Verschieben wuerde
/// intakte Daten aus dem Weg raeumen. Aufrufer catchen deshalb NUR
/// <c>JsonException</c> und rufen dann <see cref="Quarantine"/>;
/// andere Exceptions (IOException etc.) durchreichen oder
/// stumm loggen ohne Quarantine.</para>
/// </summary>
internal static class JsonFileStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Schreibt <paramref name="json"/> atomar nach
    /// <paramref name="path"/>. Legt das Zielverzeichnis an falls
    /// noetig.</summary>
    public static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tmp);
            throw;
        }
    }

    /// <summary>Verschiebt eine nicht deserialisierbare Datei nach
    /// <c>&lt;datei&gt;.broken</c>. Schlaegt das fehl (z.B. Datei
    /// gesperrt), wird nur geloggt — der Aufrufer startet in jedem Fall
    /// leer weiter.</summary>
    public static void Quarantine(string path)
    {
        var broken = path + ".broken";
        try
        {
            File.Move(path, broken, overwrite: true);
            Log.Error("Defekte JSON-Datei nach {Broken} gesichert — leerer Neustart.", broken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Defekte JSON-Datei {Path} konnte nicht nach {Broken} gesichert werden.",
                path, broken);
        }
    }

    private static void TryDeleteTemp(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); }
        catch (Exception ex) { Log.Warn(ex, "Temp-File {Tmp} konnte nicht geloescht werden.", tmp); }
    }
}
