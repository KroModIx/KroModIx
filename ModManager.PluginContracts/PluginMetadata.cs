namespace ModManager.PluginContracts;

/// <summary>Zur Laufzeit vom Plugin gemeldete Metadaten. Sollten mit dem
/// <see cref="PluginManifest"/> übereinstimmen — der Host warnt bei Divergenz.</summary>
public sealed record PluginMetadata(
    string Id,
    string DisplayName,
    string Version,
    string Author,
    string Description);
