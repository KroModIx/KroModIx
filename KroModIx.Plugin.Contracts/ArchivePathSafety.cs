using System;
using System.IO;

namespace KroModIx.Plugin.Contracts;

/// <summary>Der Ausbruch-Schutz für archiv-relative Pfade — als reine
/// Funktion, damit es ihn genau <b>einmal</b> gibt.
///
/// <para><b>Warum in den Contracts und nicht im Host-Dienst.</b> Der Schutz
/// ist Rechnung, kein Dienst: er braucht keinen Zustand, keine Abhängigkeit
/// und keine laufende Anwendung. Er lag bis v1.31.0 in
/// <c>HostArchiveServiceImpl</c>, und jede Attrappe in einem Test-Projekt
/// hätte ihn nachbauen müssen — eine Abweichung in einer dieser Kopien
/// wäre genau der Fall, in dem die Plugin-Tests grün sind und die
/// Wirklichkeit nicht. Hier liegt er für Host, Attrappe und Plugin
/// gleichermaßen.</para>
///
/// <para><b>Warum ein <c>Contains("..")</c>-Test nicht reicht</b> — am
/// 03.10.2026 am Cyberpunk-Installer nachgemessen: ein Archiv-Eintrag mit
/// <b>absolutem</b> Namen enthält kein <c>..</c>, kommt also durch, und
/// <c>Path.Combine(installDir, "/tmp/ausserhalb.txt")</c> gibt
/// <c>/tmp/ausserhalb.txt</c> zurück — das Zielverzeichnis wird verworfen.
/// Die Datei landete außerhalb des Spiels, und der Install meldete
/// <c>Success = true</c>. Dazu: ob <c>C:\…</c> als absolut gilt, hängt an
/// der Plattform, weshalb der Laufwerksbuchstabe <b>zusätzlich</b> geprüft
/// wird und das Verhalten damit auf beiden Plattformen gleich ist.</para></summary>
public static class ArchivePathSafety
{
    /// <summary>Löst <paramref name="relative"/> gegen <paramref name="root"/>
    /// auf und nimmt das Ergebnis nur an, wenn es wirklich unterhalb von
    /// root landet. Prüft den <b>Pfad</b>, nicht das Dateisystem — es muss
    /// nichts davon existieren.</summary>
    public static bool TryResolve(string root, string relative, out string destination)
    {
        destination = "";
        if (string.IsNullOrWhiteSpace(relative)) return false;
        if (HasDriveLetter(relative)) return false;

        var rel = relative.Replace('\\', Path.DirectorySeparatorChar)
                          .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(rel)) return false;

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        string full;
        try { full = Path.GetFullPath(Path.Combine(rootFull, rel)); }
        catch { return false; }

        if (!full.StartsWith(rootFull, StringComparison.Ordinal)) return false;
        destination = full;
        return true;
    }

    /// <summary>Erkennt <c>C:\…</c> und <c>C:/…</c> unabhängig von der
    /// Plattform. Ohne diese Prüfung wäre das Verhalten
    /// plattformabhängig: auf Linux ist <c>Path.IsPathRooted("C:\\x")</c>
    /// falsch, der Eintrag landete dann als Ordner namens <c>C:</c>
    /// <b>innerhalb</b> des Ziels — kein Ausbruch, aber auch nicht das, was
    /// der Test auf Windows prüft.</summary>
    public static bool HasDriveLetter(string path)
        => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
}
