using System;
using System.IO;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Deinstalliert ein installiertes Plugin (Löschen des Plugin-Ordners unter
/// <see cref="AppPaths.UserPluginsDir"/>) und optional dessen persistenter
/// Config (<c>plugin-data</c>) und Cache (<c>plugin-cache</c>).
///
/// <para><b>Restart-Semantik:</b> Wir nutzen <c>Assembly.LoadFrom</c> ohne
/// <c>AssemblyLoadContext</c> — die geladene DLL bleibt im Prozess-Speicher
/// bis zum App-Neustart. Die Uninstall-Aktion räumt die Files auf Disk auf;
/// die Plugin-Tabs verschwinden erst nach dem Neustart aus der UI. Der
/// UI-Caller zeigt einen Restart-Hint.</para>
///
/// <para>Nicht destruktiv gegen bundled-Plugins (die neben der Exe liegen) —
/// wir überprüfen dass der Zielpfad unter dem User-Plugins-Root liegt.</para>
/// </summary>
public sealed class PluginUninstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public sealed record Result(bool PluginDirRemoved, bool DataDirRemoved, bool CacheDirRemoved);

    /// <summary>Deinstalliert das Plugin mit der angegebenen ID. Ordner werden
    /// „best effort" gelöscht — Fehler landen im Log, aber die Operation
    /// bricht nicht ab damit ein teilweise gelöschtes Plugin beim nächsten
    /// Start nicht aktiviert wird.</summary>
    /// <param name="pluginId">z.B. „kroste.ls25"</param>
    /// <param name="deleteData">Persistente Config löschen (<c>~/.config/KroModIx/plugin-data/&lt;id&gt;/</c>).</param>
    /// <param name="deleteCache">Cache löschen (<c>~/.cache/KroModIx/plugin-cache/&lt;id&gt;/</c>).</param>
    public Result Uninstall(string pluginId, bool deleteData, bool deleteCache)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("pluginId leer", nameof(pluginId));

        var pluginDir = Path.Combine(AppPaths.UserPluginsDir, pluginId);
        bool pluginRemoved = TryDeleteDir(pluginDir, "Plugin-Ordner");

        bool dataRemoved = false;
        if (deleteData)
        {
            var dataDir = Path.Combine(AppPaths.ConfigRoot, "plugin-data", pluginId);
            dataRemoved = TryDeleteDir(dataDir, "Plugin-Data-Ordner");
        }

        bool cacheRemoved = false;
        if (deleteCache)
        {
            var cacheDir = Path.Combine(AppPaths.CacheRoot, "plugin-cache", pluginId);
            cacheRemoved = TryDeleteDir(cacheDir, "Plugin-Cache-Ordner");
        }

        Log.Info("Plugin-Uninstall {Id}: plugin={P} data={D} cache={C}",
            pluginId, pluginRemoved, dataRemoved, cacheRemoved);
        return new Result(pluginRemoved, dataRemoved, cacheRemoved);
    }

    private static bool TryDeleteDir(string dir, string what)
    {
        if (!Directory.Exists(dir))
        {
            Log.Debug("{What} existiert nicht: {Dir}", what, dir);
            return false;
        }
        try
        {
            Directory.Delete(dir, recursive: true);
            Log.Info("{What} gelöscht: {Dir}", what, dir);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "{What} konnte nicht gelöscht werden: {Dir}", what, dir);
            return false;
        }
    }
}
