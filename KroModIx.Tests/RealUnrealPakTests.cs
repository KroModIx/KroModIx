using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Pak;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Messung des Pak-Baukastens gegen <b>echte</b> Spieldaten.
///
/// <para>Diese Tests überspringen sich, wo kein Unreal-Spiel installiert ist
/// — also im CI-Runner. Sie setzen damit bewusst eine Entwicklungsumgebung
/// voraus, und genau deshalb dürfen sie nicht rot werden, wenn sie fehlt:
/// ein Test, der eine Steam-Installation braucht und ohne sie fehlschlägt,
/// färbt den CI-Lauf rot, ohne einen Fehler zu finden.</para>
///
/// <para><b>Warum es sie trotzdem gibt.</b> Das Pak-Format ist nicht aus
/// einer Spezifikation portiert, sondern aus einer Implementierung, die
/// gegen echte Daten verifiziert wurde. Die synthetischen Rundlauf-Tests in
/// <see cref="HostUnrealPakServiceTests"/> prüfen nur, dass Leser und
/// Schreiber zueinander passen — sie würden einen gemeinsamen Denkfehler
/// beider nicht finden. Erst das Lesen eines von Epic gekochten Paks
/// schließt das aus.</para>
///
/// <para>Stand der Messung auf dem Entwicklungsrechner (03.10.2026,
/// Icarus-Spielwoche 252): 299 von 299 Einträgen der
/// <c>Content/Data/data.pak</c> rekonstruiert, 42.274.800 Byte entpackt,
/// 52 ms. Größte Tabelle <c>Items/D_ItemsStatic.json</c> mit
/// 7.420.669 Byte.</para></summary>
public sealed class RealUnrealPakTests
{
    /// <summary>Über diese Umgebungsvariable lässt sich ein beliebiges Pak
    /// angeben — so ist der Test nicht an Icarus gebunden, sondern an „ein
    /// echtes, von der Engine ausgeliefertes Pak".</summary>
    private const string EnvVar = "KROMODIX_TEST_PAK";

    private static readonly string[] Candidates =
    [
        // Icarus' Datentabellen-Pak: klein (2,5 MB), aber gemischt
        // gespeichert und Zlib-komprimiert — der interessante Fall.
        "/run/media/system/Games/SteamLibrary/steamapps/common/Icarus/Icarus/Content/Data/data.pak",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/Steam/steamapps/common/Icarus/Icarus/Content/Data/data.pak"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".steam/steam/steamapps/common/Icarus/Icarus/Content/Data/data.pak"),
    ];

    private static string? FindPak()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
        return Candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public void JederEintragEinesEchtenPaksWirdRekonstruiert()
    {
        var path = FindPak();
        if (path is null)
            Assert.Skip($"Kein echtes Pak gefunden (setze {EnvVar}, um eines anzugeben).");

        var svc = new HostUnrealPakServiceImpl();
        svc.IsPakFile(path!).Should().BeTrue();

        using var r = svc.OpenRead(path!);
        r.Entries.Should().NotBeEmpty();
        r.IndexHash.Should().MatchRegex("^[0-9a-f]{40}$");
        // Ein vom Spiel ausgeliefertes Pak traegt den Pfad der
        // Cook-Maschine als Mount-Point — nicht die relative Mod-Form.
        r.MountPoint.Should().NotBeNullOrEmpty();

        long totalBytes = 0;
        var unsupported = 0;
        foreach (var e in r.Entries)
        {
            byte[] data;
            try
            {
                data = r.Read(e.Path);
            }
            catch (UnsupportedPakFormatException)
            {
                // Oodle — ein erwarteter, lauter Fehler und kein
                // Testfehlschlag. Icarus' data.pak hat keinen solchen
                // Eintrag, die pakchunks dagegen schon.
                unsupported++;
                continue;
            }
            catch (Exception ex)
            {
                Assert.Fail($"{e.Path}: {ex.GetType().Name}: {ex.Message}");
                return;
            }
            // Die gemeldete Groesse ist die entpackte — stimmt sie nicht, ist
            // entweder der Index falsch gelesen oder die Dekompression
            // unvollstaendig.
            data.Length.Should().Be((int)e.Size, e.Path);
            totalBytes += data.Length;
        }

        (r.Entries.Count - unsupported).Should().BeGreaterThan(0,
            "mindestens ein Eintrag muss lesbar sein, sonst misst dieser Test nichts");
        totalBytes.Should().BeGreaterThan(1_000_000);
    }

    /// <summary>Liest Paks, die ein <b>anderes</b> Werkzeug geschrieben hat
    /// — der stärkere Beleg, weil er einen gemeinsamen Denkfehler von
    /// eigenem Leser und eigenem Schreiber ausschließt.</summary>
    [Fact]
    public void FremdGeschriebeneModPaksSindLesbar()
    {
        var basePak = FindPak();
        if (basePak is null) Assert.Skip("Kein echtes Pak gefunden.");

        // Der Mods-Ordner liegt bei Icarus zwei Ebenen ueber Content/Data.
        var contentDir = Directory.GetParent(basePak!)?.Parent?.FullName;
        var modsDir = contentDir is null ? null : Path.Combine(contentDir, "Paks", "mods");
        if (modsDir is null || !Directory.Exists(modsDir))
            Assert.Skip("Kein Mods-Ordner neben dem Pak.");

        var foreign = Directory.EnumerateFiles(modsDir, "*_P.pak").ToList();
        if (foreign.Count == 0) Assert.Skip("Keine Mod-Paks im Mods-Ordner.");

        var svc = new HostUnrealPakServiceImpl();
        foreach (var path in foreign)
        {
            using var r = svc.OpenRead(path);
            r.Entries.Should().NotBeEmpty(Path.GetFileName(path));
            foreach (var e in r.Entries)
            {
                try { r.Read(e.Path).Length.Should().Be((int)e.Size, e.Path); }
                catch (UnsupportedPakFormatException) { /* Oodle: erwartet */ }
            }
        }
    }
}
