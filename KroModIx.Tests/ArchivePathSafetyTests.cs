using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.TestKit;
using KroModIx.Services.Archive;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Der Ausbruch-Schutz liegt seit v1.32.0 als reine Funktion in den
/// Contracts. Geprüft wird hier beides: dass die Funktion selbst richtig
/// entscheidet, und dass alle drei Stellen, die sie benutzen, dieselbe
/// Antwort geben.</summary>
public class ArchivePathSafetyTests
{
    /// <summary>Die Fälle, die wirklich vorkommen — und der, der die
    /// Migration ausgelöst hat.</summary>
    public static TheoryData<string, bool> Faelle => new()
    {
        { "mods/MeineMod/info.json", true },
        { @"mods\MeineMod\info.json", true },
        { "unterordner/./datei.txt", true },
        { "a/b/../c.txt", true },

        { "../ausbruch.txt", false },
        { "mods/../../ausbruch.txt", false },
        // Der gemessene Fall: ein absoluter Eintragsname enthaelt kein „..",
        // kommt an einem Contains("..")-Test vorbei, und Path.Combine
        // verwirft dann das Zielverzeichnis. Am 03.10.2026 am
        // Cyberpunk-Installer nachgewiesen: Datei lag ausserhalb des
        // InstallDir, der Install meldete Erfolg.
        { "/etc/passwd", false },
        { "/tmp/ausserhalb.txt", false },
        // Laufwerksbuchstaben gelten auf Linux nicht als absolut — ohne die
        // zusaetzliche Pruefung waere das Verhalten plattformabhaengig.
        { @"C:\windows\system32\evil.dll", false },
        { "C:/windows/evil.dll", false },
        // Laufwerks-relativ: auf Windows loest das gegen das aktuelle
        // Verzeichnis von C: auf, ist also ebenfalls ein Ausbruch.
        { "C:evil.dll", false },
        { "", false },
        { "   ", false },
    };

    [Theory]
    [MemberData(nameof(Faelle))]
    public void DieFunktionEntscheidetRichtig(string eintrag, bool erwartetSicher)
    {
        var root = Path.Combine(Path.GetTempPath(), "kromodix-safety-root");
        ArchivePathSafety.TryResolve(root, eintrag, out var ziel).Should().Be(erwartetSicher);
        if (erwartetSicher)
            ziel.Should().StartWith(Path.GetFullPath(root));
        else
            ziel.Should().BeEmpty("ohne Freigabe darf kein Pfad zurückkommen, "
                + "sonst benutzt ein Aufrufer ihn trotzdem");
    }

    /// <summary>Host-Dienst, Attrappe und Null-Implementierung müssen
    /// dieselbe Antwort geben. Sie tun es seit v1.32.0 per Bauart — alle
    /// drei rufen dieselbe Funktion —, und genau das hält dieser Test fest:
    /// baut später jemand eine eigene Kopie ein, fällt es hier auf und nicht
    /// erst in einem Plugin-Test, der grün bleibt.</summary>
    [Theory]
    [MemberData(nameof(Faelle))]
    public void AlleDreiStellenAntwortenGleich(string eintrag, bool erwartetSicher)
    {
        var root = Path.Combine(Path.GetTempPath(), "kromodix-safety-root");
        new HostArchiveServiceImpl().TryResolveSafe(root, eintrag, out var a)
            .Should().Be(erwartetSicher);
        new FakeArchiveService().TryResolveSafe(root, eintrag, out var b)
            .Should().Be(erwartetSicher);
        NullArchiveService.Instance.TryResolveSafe(root, eintrag, out var c)
            .Should().Be(erwartetSicher);
        b.Should().Be(a);
        c.Should().Be(a);
    }
}
