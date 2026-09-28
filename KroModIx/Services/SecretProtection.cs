using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services;

/// <summary>
/// Schützt Secrets (API-Keys, Cookies) für die persistente JSON-Ablage.
/// Windows: DPAPI (Per-User-Scope). Linux/macOS: AES mit einem Schlüssel aus einer Datei
/// (<c>geheim.key</c> im Konfigurationsordner, 0600). Format: <c>"v2:&lt;base64&gt;"</c>.
///
/// **Warum eine Datei und nicht mehr der Rechnername** (übernommen aus KaetheronBot, 22.09.2026):
/// der Schlüssel kam aus <c>MachineName|UserName|kroste-modmanager</c>. Auf Bazzite ist
/// <c>/etc/hostname</c> leer, systemd nimmt „bazzite", die FRITZ!Box setzt per DHCP
/// „bazzite.fritz.box" - je nach Startzeitpunkt ein anderer Name, und danach ließ sich kein
/// gespeichertes Secret mehr lesen („Padding is invalid"). Die Datei ändert sich nie.
///
/// Alte <c>v1:</c>-Werte werden weiter gelesen: unter Windows mit DPAPI, sonst mit allen
/// plausiblen Namensformen (kurz, mit Domäne, aus DNS, aus <c>/etc/hostname</c>).
/// <see cref="Renew"/> hebt sie beim Laden auf <c>v2:</c>.
/// </summary>
public sealed class SecretProtection : ISecretProtection
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly byte[] Salt = "kroste-modmanager-secret-v1"u8.ToArray();
    private static byte[]? _key;

    /// <summary>Die Schlüsseldatei; über <c>KROMODIX_SECRET_KEY</c> umlenkbar (Tests, Container).</summary>
    private static string KeyFile
        => Environment.GetEnvironmentVariable("KROMODIX_SECRET_KEY") is { Length: > 0 } own
            ? own : Path.Combine(AppPaths.ConfigRoot, "geheim.key");

    /// <summary>Nur für Tests, die die Datei wechseln.</summary>
    internal static void ForgetKey() => _key = null;

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        try
        {
            byte[] cipher = OperatingSystem.IsWindows()
                ? ProtectWindows(Encoding.UTF8.GetBytes(plaintext))
                : ProtectAes(Encoding.UTF8.GetBytes(plaintext));
            return (OperatingSystem.IsWindows() ? "v1:" : "v2:") + Convert.ToBase64String(cipher);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Secret konnte nicht verschlüsselt werden — fällt auf null zurück");
            return null;
        }
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        if (!ciphertext.StartsWith("v1:", StringComparison.Ordinal)
            && !ciphertext.StartsWith("v2:", StringComparison.Ordinal)) return null;
        try
        {
            byte[] cipher = Convert.FromBase64String(ciphertext[3..]);
            if (OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(UnprotectWindows(cipher));
            if (ciphertext.StartsWith("v2:", StringComparison.Ordinal))
                return Encoding.UTF8.GetString(UnprotectAes(cipher, DeriveKey()));
            // Alter Wert: mit jeder plausiblen Namensform versuchen
            foreach (var key in LegacyKeys())
            {
                try
                {
                    var plain = Encoding.UTF8.GetString(UnprotectAes(cipher, key));
                    if (!plain.Any(char.IsControl)) return plain;
                }
                catch (CryptographicException) { /* nächster Name */ }
            }
            Log.Warn("Alter Secret-Wert ließ sich mit keiner Namensform lesen - bitte neu eingeben");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Secret konnte nicht entschlüsselt werden — Fallback null " +
                "(Maschinen-/User-Wechsel oder korrupte Config)");
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plain)
        => ProtectedData.Protect(plain, Salt, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] cipher)
        => ProtectedData.Unprotect(cipher, Salt, DataProtectionScope.CurrentUser);

    /// <summary>
    /// Hebt einen alten <c>v1:</c>-Wert auf <c>v2:</c>, sobald er sich lesen ließ - sonst hinge er
    /// weiter am Rechnernamen. Gibt den Wert unverändert zurück, wenn nichts zu tun ist.
    /// </summary>
    public string? Renew(string? ciphertext)
    {
        if (OperatingSystem.IsWindows() || ciphertext is null || !ciphertext.StartsWith("v1:", StringComparison.Ordinal))
            return ciphertext;
        return Unprotect(ciphertext) is { } plain ? Protect(plain) : ciphertext;
    }

    /// <summary>Die Namensformen, unter denen ein alter Wert entstanden sein kann.</summary>
    internal static IEnumerable<byte[]> LegacyKeys()
    {
        foreach (var name in LegacyNames(new[] { Environment.MachineName, HostFromDns(), HostFromEtc() }))
            yield return SHA256.HashData(Encoding.UTF8.GetBytes($"{name}|{Environment.UserName}|kroste-modmanager"));
    }

    /// <summary>Kurz und mit Domäne, ohne Doppelte - als reine Funktion, damit ein Test sie prüfen kann.</summary>
    internal static List<string> LegacyNames(IEnumerable<string?> raw)
    {
        var aus = new List<string>();
        foreach (var n in raw)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            var name = n.Trim();
            if (!aus.Contains(name)) aus.Add(name);
            var kurz = name.Split('.')[0];
            if (kurz.Length > 0 && !aus.Contains(kurz)) aus.Add(kurz);
        }
        return aus;
    }

    private static string? HostFromDns()
    {
        try { return System.Net.Dns.GetHostName(); }
        catch (System.Net.Sockets.SocketException) { return null; }
    }

    private static string? HostFromEtc()
    {
        try { return File.Exists("/etc/hostname") ? File.ReadAllText("/etc/hostname").Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static byte[] ProtectAes(byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        byte[] body = enc.TransformFinalBlock(plain, 0, plain.Length);
        byte[] result = new byte[aes.IV.Length + body.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(body, 0, result, aes.IV.Length, body.Length);
        return result;
    }

    private static byte[] UnprotectAes(byte[] cipher, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] iv = new byte[16];
        Buffer.BlockCopy(cipher, 0, iv, 0, iv.Length);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, iv.Length, cipher.Length - iv.Length);
    }

    /// <summary>
    /// Der Schlüssel aus der Datei; fehlt sie, entsteht sie einmalig mit 32 Zufallsbytes. Ist sie da,
    /// aber nicht lesbar, **wirft das** - still einen neuen zu nehmen machte jedes gespeicherte Secret
    /// unlesbar, und man sähe nur „Secret konnte nicht entschlüsselt werden".
    /// </summary>
    internal static byte[] KeyBytes(string file)
    {
        if (File.Exists(file))
        {
            try
            {
                return File.ReadAllBytes(file);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Log.Error(ex, "Die Schlüsseldatei {0} ist da, aber nicht lesbar", file);
                throw new InvalidOperationException($"Die Schlüsseldatei {file} ist vorhanden, aber nicht lesbar - Rechte prüfen.", ex);
            }
        }
        var neu = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(file) is { Length: > 0 } dir ? dir : AppPaths.ConfigRoot);
        File.WriteAllBytes(file, neu);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Log.Info("Neuer Schlüssel für die Secrets angelegt");
        return neu;
    }

    private static byte[] DeriveKey()
        => _key ??= SHA256.HashData([.. KeyBytes(KeyFile), .. Salt]);
}
