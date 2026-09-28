namespace KroModIx.Plugin.Contracts;

/// <summary>Verschlüsselt Plugin-Secrets (API-Keys, Cookies) für die persistente
/// Ablage. Windows: DPAPI (<c>v1:</c>). Linux/macOS: AES mit einem Schlüssel aus einer
/// Datei im Konfigurationsordner (<c>v2:</c>) - alte <c>v1:</c>-Werte, die am Rechnernamen
/// hingen, werden weiter gelesen und beim Speichern gehoben.</summary>
public interface ISecretProtection
{
    string? Protect(string? plaintext);
    string? Unprotect(string? ciphertext);
}
