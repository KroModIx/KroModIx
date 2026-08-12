using System;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Nexus;

/// <summary>Speichert den Nexus-API-Key verschlüsselt in
/// <see cref="AppSettings.NexusApiKeyProtected"/>. Der Key wird bei jedem
/// <see cref="GetApiKey"/> live entschlüsselt (kein persistenter Klartext
/// im Prozess-Speicher außerhalb der Aufrufer-Frame).
///
/// <para>Nach <see cref="Save"/> oder <see cref="Delete"/> wird
/// <see cref="HostNexusServiceImpl.RaiseApiKeyChanged"/> gefeuert, damit
/// die Plugins ihre Katalog-Views neu laden.</para></summary>
public sealed class NexusApiKeyStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsService _settings;
    private readonly ISecretProtection _secrets;
    private HostNexusServiceImpl? _nexus;

    public NexusApiKeyStore(AppSettingsService settings, ISecretProtection secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>Wird beim App-Bootstrap gesetzt (dank Circular-Dependency
    /// zwischen Store und Nexus-Impl kann's nicht per ctor). Wenn null,
    /// werden keine ApiKeyChanged-Events gefeuert — das ist ok, Nexus-Impl
    /// prüft <c>HasApiKey</c> ohnehin bei jedem Call.</summary>
    public void SetNexus(HostNexusServiceImpl nexus) => _nexus = nexus;

    public string GetApiKey()
    {
        var encrypted = _settings.Current.NexusApiKeyProtected;
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        try { return _secrets.Unprotect(encrypted) ?? string.Empty; }
        catch (Exception ex)
        {
            Log.Warn(ex, "NexusApiKeyStore: Unprotect fehlgeschlagen — Key wird als leer behandelt");
            return string.Empty;
        }
    }

    /// <summary>Verschlüsselt + persistiert den Key. Feuert
    /// <c>ApiKeyChanged</c> damit Plugins reagieren.</summary>
    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { Delete(); return; }
        var protectedKey = _secrets.Protect(apiKey.Trim());
        _settings.Update(s => s.NexusApiKeyProtected = protectedKey);
        _nexus?.RaiseApiKeyChanged();
        Log.Info("Nexus-API-Key gespeichert (Länge: {L} Chars verschlüsselt)",
            protectedKey?.Length ?? 0);
    }

    public void Delete()
    {
        _settings.Update(s => s.NexusApiKeyProtected = null);
        _nexus?.RaiseApiKeyChanged();
        Log.Info("Nexus-API-Key gelöscht.");
    }
}
