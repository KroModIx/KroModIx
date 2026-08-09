using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services;

/// <summary>
/// Schützt Secrets (API-Keys, Cookies) für die persistente JSON-Ablage.
/// Windows: DPAPI (Per-User-Scope). Linux/macOS: AES mit deterministischem
/// Schlüssel aus MachineName + UserName + statischer App-Konstante. Nicht so
/// sicher wie ein echter Keyring, aber verhindert, dass ein versehentlich
/// veröffentlichter Config-Dump die Keys direkt preisgibt.
/// Format: "v1:&lt;base64&gt;".
/// </summary>
public sealed class SecretProtection : ISecretProtection
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly byte[] Salt = "kroste-modmanager-secret-v1"u8.ToArray();

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        try
        {
            byte[] cipher = OperatingSystem.IsWindows()
                ? ProtectWindows(Encoding.UTF8.GetBytes(plaintext))
                : ProtectAes(Encoding.UTF8.GetBytes(plaintext));
            return "v1:" + Convert.ToBase64String(cipher);
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
        if (!ciphertext.StartsWith("v1:", StringComparison.Ordinal)) return null;
        try
        {
            byte[] cipher = Convert.FromBase64String(ciphertext[3..]);
            byte[] plain = OperatingSystem.IsWindows()
                ? UnprotectWindows(cipher)
                : UnprotectAes(cipher);
            return Encoding.UTF8.GetString(plain);
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

    private static byte[] UnprotectAes(byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        byte[] iv = new byte[16];
        Buffer.BlockCopy(cipher, 0, iv, 0, iv.Length);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, iv.Length, cipher.Length - iv.Length);
    }

    private static byte[] DeriveKey()
    {
        string material = $"{Environment.MachineName}|{Environment.UserName}|kroste-modmanager";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }
}
