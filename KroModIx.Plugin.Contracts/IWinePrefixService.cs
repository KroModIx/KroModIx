namespace KroModIx.Plugin.Contracts;

/// <summary>Was beim Setzen einer DLL-Umleitung herauskam.</summary>
public enum DllOverrideResult
{
    /// <summary>Eingetragen — wirkt beim nächsten Spielstart.</summary>
    Added,
    /// <summary>Stand schon drin, nichts zu tun.</summary>
    AlreadySet,
    /// <summary>Kein Präfix vorhanden: Windows, oder das Spiel wurde unter
    /// Linux noch nie gestartet.</summary>
    NoPrefix,
    /// <summary>Präfix da, aber die Registry-Datei ist nicht lesbar oder
    /// nicht beschreibbar.</summary>
    Failed,
}

/// <summary>Zentraler Baukasten für das Proton-/Wine-Präfix eines Spiels
/// (v1.30.0+). Aktuell: DLL-Umleitungen setzen.
///
/// <para><b>Warum im Host.</b> Mod-Loader für Unreal-Spiele hängen sich fast
/// alle über eine mitgelieferte System-DLL ein, die neben der Spiel-Exe
/// liegt — UE4SS über <c>dwmapi.dll</c>. Proton bringt dieselbe DLL mit und
/// bevorzugt sie, der Loader wird also nie geladen. Es gibt dafür kein
/// Symptom außer Abwesenheit: das Spiel startet normal, keine
/// Fehlermeldung, keine Logdatei, die Mods tun nichts. Jedes Plugin für ein
/// UE-Spiel mit UE4SS braucht genau diese Umleitung — Icarus heute,
/// Satisfactory und Schedule I als nächste.</para>
///
/// <para><b>Warum die Registry und nicht die Steam-Startoptionen.</b> Die
/// verbreitete Anleitung setzt <c>WINEDLLOVERRIDES="dwmapi=n,b"</c> als
/// Startoption. Das steht in Steams <c>localconfig.vdf</c>, die ein
/// laufender Steam-Client beim Beenden aus dem Speicher zurückschreibt —
/// eine Änderung von außen wäre verloren, solange Steam läuft, und das tut
/// es, wenn der User gerade moddet. Der Eintrag in der <c>user.reg</c> des
/// Präfix wirkt sofort und braucht keinen Steam-Neustart.</para>
///
/// <para><b>Grenze, die das Plugin dem User sagen muss:</b> legt Proton das
/// Präfix neu an (Proton-Wechsel, Reset über „Spieldateien überprüfen"), ist
/// der Eintrag weg und der Loader lädt wieder stillschweigend nicht. Ein
/// Plugin soll den Zustand deshalb bei jedem Refresh neu prüfen statt sich
/// das Ergebnis zu merken.</para></summary>
public interface IWinePrefixService
{
    /// <summary>Der Standardwert für eine Loader-Umleitung: erst die DLL
    /// neben der Exe, dann die von Wine.</summary>
    const string NativeThenBuiltin = "native,builtin";

    /// <summary>Ob für <paramref name="dllName"/> (ohne <c>.dll</c>) im
    /// Präfix eine Umleitung gesetzt ist. Fehlendes Präfix und fehlende
    /// Registry-Datei zählen als „nicht gesetzt" — für den Aufrufer ist das
    /// dasselbe.</summary>
    bool IsDllOverrideSet(string? prefixPath, string dllName);

    /// <summary>Setzt die Umleitung, falls sie fehlt. Idempotent: ein
    /// zweiter Aufruf liefert <see cref="DllOverrideResult.AlreadySet"/> und
    /// schreibt nicht.
    ///
    /// <para>Der Host sichert die Registry-Datei einmalig, bevor er sie zum
    /// ersten Mal anfasst — sie ist die einzige Datei, die hier in einem
    /// fremden Präfix verändert wird.</para></summary>
    /// <param name="prefixPath">Der Präfix-Pfad, also
    /// <c>DetectedGame.ProtonPrefix</c>. Null oder leer ergibt
    /// <see cref="DllOverrideResult.NoPrefix"/>.</param>
    /// <param name="dllName">Der DLL-Name ohne Endung, etwa
    /// <c>dwmapi</c>.</param>
    /// <param name="value">Der Wine-Override-Wert; Standard ist
    /// <see cref="NativeThenBuiltin"/>.</param>
    DllOverrideResult EnsureDllOverride(string? prefixPath, string dllName,
        string value = NativeThenBuiltin);
}

/// <summary>Default für Hosts &lt; v1.30.0. Die Abfrage antwortet
/// <c>false</c>, das Setzen scheitert laut — eine folgenlos „gesetzte"
/// Umleitung wäre genau der stille Ausfall, den dieser Baukasten
/// verhindern soll.</summary>
public sealed class NullWinePrefixService : IWinePrefixService
{
    public static readonly NullWinePrefixService Instance = new();
    private NullWinePrefixService() { }

    public bool IsDllOverrideSet(string? prefixPath, string dllName) => false;

    public DllOverrideResult EnsureDllOverride(string? prefixPath, string dllName,
        string value = IWinePrefixService.NativeThenBuiltin)
        => throw new HostFeatureUnavailableException(
            "Wine-Präfix-Baukasten (IWinePrefixService)", "1.30.0");
}
