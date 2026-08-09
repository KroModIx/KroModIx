namespace KroModIx.Plugin.Contracts;

/// <summary>Verschlüsselt Plugin-Secrets (API-Keys, Cookies) für die persistente
/// Ablage. Windows: DPAPI. Linux/macOS: AES mit Machine/User-Bindung.
/// Format: <c>v1:&lt;base64&gt;</c>.</summary>
public interface ISecretProtection
{
    string? Protect(string? plaintext);
    string? Unprotect(string? ciphertext);
}
