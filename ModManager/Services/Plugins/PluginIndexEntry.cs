using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModManager.Services.Plugins;

/// <summary>Deserialisierte Kopie der plugins.json aus <c>Kroste/ModManager.PluginIndex</c>.</summary>
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

    [JsonPropertyName("updateSource")]
    public PluginIndexUpdateSource? UpdateSource { get; set; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }
}

public sealed class PluginIndexUpdateSource
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "github";

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;
}
