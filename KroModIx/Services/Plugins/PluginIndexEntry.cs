using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KroModIx.Services.Plugins;

/// <summary>Deserialisierte Kopie der plugins.json aus <c>KroModIx/KroModIx.PluginIndex</c>.</summary>
public sealed class PluginIndex
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("plugins")]
    public List<PluginIndexEntry> Plugins { get; set; } = new();
}

public sealed class PluginIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("steamAppIds")]
    public List<int> SteamAppIds { get; set; } = new();

    /// <summary>v1.28.1: Engine-Slugs (z. B. <c>["renpy"]</c>) — das Index-
    /// Gegenstueck zu <c>PluginManifest.Targets[].Engine</c>. Ordner-basierte
    /// Sammlungen (Wizard „🎮 Ordner mit Spielen scannen") legen Manual-Games
    /// OHNE SteamAppId an; ohne dieses Feld findet der Host kein Index-Plugin
    /// fuer sie und zeigt statt der Install-Karte „kein Plugin verfuegbar".
    /// Fehlt im Index-JSON → leere Liste, reines SteamAppId-Matching.</summary>
    [JsonPropertyName("engines")]
    public List<string> Engines { get; set; } = new();

    [JsonPropertyName("updateSource")]
    public PluginIndexUpdateSource? UpdateSource { get; set; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>v1.11: Genre-Tags (z. B. <c>["visual-novel", "adult"]</c>).
    /// Werden im InstallCard als Chips gerendert und im PluginUpdates-Window
    /// als Filter-Facette angeboten. Fehlt im Index-JSON → leere Liste,
    /// kein Rendering (rueckwaerts-kompatibel).</summary>
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();
}

public sealed class PluginIndexUpdateSource
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "github";

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;
}
