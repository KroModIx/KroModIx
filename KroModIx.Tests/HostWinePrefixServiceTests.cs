using System;
using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Wine;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Die DLL-Umleitung ist die Stelle, an der ein Mod-Loader unter
/// Proton steht oder fällt — und ihr Fehlen hat kein Symptom außer
/// Wirkungslosigkeit. Deshalb geprüft gegen eine echte
/// <c>user.reg</c>-Struktur, nicht gegen eine vereinfachte.</summary>
public sealed class HostWinePrefixServiceTests : IDisposable
{
    private readonly string _prefix = Directory.CreateTempSubdirectory("kromodix-pfx").FullName;
    private readonly IWinePrefixService _svc = new HostWinePrefixServiceImpl();

    public void Dispose()
    {
        try { Directory.Delete(_prefix, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    /// <summary>Auszug aus einer echten Proton-<c>user.reg</c> (Icarus-Präfix
    /// auf Bazzite, 03.10.2026) — samt Zeitstempel hinter dem Schlüssel und
    /// <c>#time</c>-Zeile, damit die Einfüge-Position stimmt.</summary>
    private const string RealisticUserReg = """
        WINE REGISTRY Version 2
        ;; All keys relative to \\User\\S-1-5-21-0-0-0-1000

        #arch=win64

        [Software\\Wine\\DllOverrides] 1774341805
        #time=1dcbb6a47554ef2
        "api-ms-win-crt-conio-l1-1-0"="native,builtin"
        "atiadlxx"="disabled"
        "msvcp100"="native,builtin"

        [Software\\Wine\\Drivers] 1774341800
        #time=1dcbb6a47554ef0
        "Graphics"="x11,wayland"

        """;

    private string WriteUserReg(string content)
    {
        var path = Path.Combine(_prefix, "user.reg");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void TraegtDieUmleitungInDieBestehendeSektionEin()
    {
        var path = WriteUserReg(RealisticUserReg);

        _svc.IsDllOverrideSet(_prefix, "dwmapi").Should().BeFalse();
        _svc.EnsureDllOverride(_prefix, "dwmapi").Should().Be(DllOverrideResult.Added);
        _svc.IsDllOverrideSet(_prefix, "dwmapi").Should().BeTrue();

        var lines = File.ReadAllLines(path);
        var section = Array.FindIndex(lines, l => l.StartsWith(@"[Software\\Wine\\DllOverrides]"));
        section.Should().BeGreaterThan(-1);
        // Direkt nach der #time-Zeile, nicht irgendwo in der Datei — sonst
        // landet der Eintrag in einer fremden Sektion.
        lines[section + 1].Should().StartWith("#time=");
        lines[section + 2].Should().Be(@"""dwmapi""=""native,builtin""");

        File.ReadAllText(path).Should().Contain(@"""atiadlxx""=""disabled""")
            .And.Contain(@"[Software\\Wine\\Drivers]")
            .And.Contain(@"""Graphics""=""x11,wayland""");
    }

    [Fact]
    public void ZweiterAufrufSchreibtNicht()
    {
        var path = WriteUserReg(RealisticUserReg);
        _svc.EnsureDllOverride(_prefix, "dwmapi").Should().Be(DllOverrideResult.Added);
        var after = File.ReadAllText(path);

        _svc.EnsureDllOverride(_prefix, "dwmapi").Should().Be(DllOverrideResult.AlreadySet);
        File.ReadAllText(path).Should().Be(after);
    }

    [Fact]
    public void SichertDieDateiEinmalVorDerAenderung()
    {
        var path = WriteUserReg(RealisticUserReg);
        _svc.EnsureDllOverride(_prefix, "dwmapi");

        var backup = path + ".kromodix-backup";
        File.Exists(backup).Should().BeTrue();
        File.ReadAllText(backup).Should().Be(RealisticUserReg,
            "die Sicherung muss den Stand VOR der Änderung halten");
    }

    [Fact]
    public void LegtDieSektionAnWennSieFehlt()
    {
        var path = WriteUserReg("""
            WINE REGISTRY Version 2

            #arch=win64

            [Software\\Wine\\Drivers] 1774341800
            #time=1dcbb6a47554ef0
            "Graphics"="x11,wayland"

            """);

        _svc.EnsureDllOverride(_prefix, "dwmapi").Should().Be(DllOverrideResult.Added);
        _svc.IsDllOverrideSet(_prefix, "dwmapi").Should().BeTrue();

        File.ReadAllText(path).Should().Contain(@"[Software\\Wine\\DllOverrides]")
            .And.Contain(@"""dwmapi""=""native,builtin""")
            .And.Contain(@"""Graphics""=""x11,wayland""");
    }

    /// <summary>Derselbe Schlüsselname kann in anderen Sektionen vorkommen.
    /// Ein Treffer dort darf nicht als „ist schon gesetzt" gelten — sonst
    /// bliebe die Umleitung aus und der Loader lädt nie.</summary>
    [Fact]
    public void TrefferInFremderSektionZaehltNicht()
    {
        WriteUserReg("""
            WINE REGISTRY Version 2

            [Software\\Wine\\Fonts] 1774341800
            #time=1dcbb6a47554ef0
            "dwmapi"="native,builtin"

            [Software\\Wine\\DllOverrides] 1774341805
            #time=1dcbb6a47554ef2
            "atiadlxx"="disabled"

            """);

        _svc.IsDllOverrideSet(_prefix, "dwmapi").Should().BeFalse();
        _svc.EnsureDllOverride(_prefix, "dwmapi").Should().Be(DllOverrideResult.Added);
    }

    [Fact]
    public void OhnePraefixOderDateiKeinSchreibversuch()
    {
        _svc.EnsureDllOverride(null, "dwmapi").Should().Be(DllOverrideResult.NoPrefix);
        _svc.EnsureDllOverride("", "dwmapi").Should().Be(DllOverrideResult.NoPrefix);
        // Praefix-Ordner da, user.reg fehlt (Spiel nie gestartet).
        _svc.EnsureDllOverride(_prefix, "dwmapi").Should().Be(DllOverrideResult.NoPrefix);
        _svc.IsDllOverrideSet(_prefix, "dwmapi").Should().BeFalse();
    }

    [Fact]
    public void AndereDllsSindUnabhaengig()
    {
        WriteUserReg(RealisticUserReg);
        // xinput1_3 ist der Einhaengepunkt der UE4SS-2.x-Reihe.
        _svc.EnsureDllOverride(_prefix, "xinput1_3").Should().Be(DllOverrideResult.Added);

        _svc.IsDllOverrideSet(_prefix, "xinput1_3").Should().BeTrue();
        _svc.IsDllOverrideSet(_prefix, "dwmapi").Should().BeFalse();
    }

    [Fact]
    public void EinAbweichenderWertWirdUebernommen()
    {
        WriteUserReg(RealisticUserReg);
        _svc.EnsureDllOverride(_prefix, "d3d11", "builtin").Should().Be(DllOverrideResult.Added);
        File.ReadAllText(Path.Combine(_prefix, "user.reg"))
            .Should().Contain(@"""d3d11""=""builtin""");
    }
}
