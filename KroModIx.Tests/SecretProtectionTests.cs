using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using KroModIx.Services;
using Xunit;

namespace KroModIx.Tests;

/// <summary>
/// Der Schlüssel hängt seit 28.09.2026 an einer Datei, nicht mehr am Rechnernamen (übernommen aus
/// KaetheronBot): auf Bazzite wechselt der Name zwischen „bazzite" und „bazzite.fritz.box", und danach
/// war jedes gespeicherte Secret unlesbar.
///
/// **Keine Testklasse fasst <c>KROMODIX_SECRET_KEY</c> an** - xunit lässt Klassen parallel laufen, und eine
/// zurückgesetzte Variable zöge anderen Tests den Schlüssel weg. Geprüft werden deshalb die reinen Funktionen.
/// </summary>
public class SecretProtectionTests
{
    [Fact]
    public void Ein_geschuetzter_Wert_kommt_unveraendert_zurueck()
    {
        var alt = Environment.GetEnvironmentVariable("KROMODIX_SECRET_KEY");
        var datei = Path.Combine(Path.GetTempPath(), "kromodix-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("KROMODIX_SECRET_KEY", datei);
            SecretProtection.ForgetKey();
            var s = new SecretProtection();

            var geschuetzt = s.Protect("nexus-api-key-123");

            geschuetzt.Should().StartWith(OperatingSystem.IsWindows() ? "v1:" : "v2:");
            s.Unprotect(geschuetzt).Should().Be("nexus-api-key-123");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KROMODIX_SECRET_KEY", alt);
            SecretProtection.ForgetKey();
            File.Delete(datei);
        }
    }

    [Fact]
    public void Eine_fehlende_Schluesseldatei_entsteht_mit_32_zufaelligen_Bytes()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "kromodix-key-" + Guid.NewGuid().ToString("N"));
        var datei = Path.Combine(ordner, "geheim.key");
        try
        {
            var a = SecretProtection.KeyBytes(datei);
            var b = SecretProtection.KeyBytes(datei);

            a.Should().HaveCount(32).And.NotEqual(new byte[32]);
            b.Should().Equal(a, "der zweite Aufruf liest, er würfelt nicht neu");
        }
        finally
        {
            Directory.Delete(ordner, true);
        }
    }

    [Fact]
    public void Eine_unlesbare_Schluesseldatei_wird_gemeldet_statt_ersetzt()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var datei = Path.Combine(Path.GetTempPath(), "kromodix-key-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(datei, new byte[32]);
        File.SetUnixFileMode(datei, UnixFileMode.None);
        try
        {
            var tat = () => SecretProtection.KeyBytes(datei);

            tat.Should().Throw<InvalidOperationException>().WithMessage("*nicht lesbar*");
        }
        finally
        {
            File.SetUnixFileMode(datei, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(datei);
        }
    }

    [Fact]
    public void Die_alten_Namensformen_enthalten_kurz_und_mit_Domaene()
    {
        var namen = SecretProtection.LegacyNames(["bazzite.fritz.box", "bazzite", null, "  "]);

        namen.Should().Equal("bazzite.fritz.box", "bazzite");
    }
}
