using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModManager.PluginContracts;

/// <summary>
/// Manifest neben der Plugin-DLL (<c>plugin.json</c>). Der Host liest das
/// Manifest OHNE die Assembly zu laden — für Discovery/Filtering/Update-Check.
/// Aktuelles Schema: <see cref="CurrentSchema"/>.
/// </summary>
public sealed class PluginManifest
{
    public const int CurrentSchema = 1;

    /// <summary>Version des Manifest-Schemas. Für zukünftige Migrationen.</summary>
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = CurrentSchema;

    /// <summary>Stabile Plugin-ID (z.B. <c>kroste.ls25</c>), muss über Updates
    /// hinweg konstant bleiben. Wird für Konflikt-Auflösung und Settings-Persistenz benutzt.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.0";

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Datei-Name der Plugin-DLL (relativ zum Manifest-Ordner).</summary>
    [JsonPropertyName("assembly")]
    public string Assembly { get; set; } = string.Empty;

    /// <summary>Voll-qualifizierter Typname der <see cref="IGameModPlugin"/>-Implementierung.</summary>
    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = string.Empty;

    /// <summary>Mindest-Host-Version, gegen die dieses Plugin gebaut ist. Der
    /// Host überspringt Plugins mit höherer <c>minHostVersion</c> als die eigene.</summary>
    [JsonPropertyName("minHostVersion")]
    public string MinHostVersion { get; set; } = "0.0.0";

    /// <summary>Spiele, die dieses Plugin bedient. Aus dem Manifest kann der
    /// Host discovern, ohne die DLL zu laden.</summary>
    [JsonPropertyName("targets")]
    public List<GameTarget> Targets { get; set; } = new();

    /// <summary>Wo der Host neue Versionen dieses Plugins holt (ab M4 nutzbar).</summary>
    [JsonPropertyName("updateSource")]
    public PluginUpdateSource? UpdateSource { get; set; }

    /// <summary>Optionaler SHA-256-Hash der Plugin-DLL. Reserviert für ein
    /// zukünftiges Trust-Model; wird in M2/M3 nicht geprüft.</summary>
    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PluginManifest FromJson(string json)
    {
        var m = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts)
            ?? throw new InvalidDataException("Leeres Plugin-Manifest.");
        if (string.IsNullOrWhiteSpace(m.Id))
            throw new InvalidDataException("plugin.json: 'id' fehlt.");
        if (string.IsNullOrWhiteSpace(m.Assembly))
            throw new InvalidDataException("plugin.json: 'assembly' fehlt.");
        if (string.IsNullOrWhiteSpace(m.EntryType))
            throw new InvalidDataException("plugin.json: 'entryType' fehlt.");
        return m;
    }

    public static PluginManifest FromFile(string path) => FromJson(File.ReadAllText(path));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
}

public sealed class PluginUpdateSource
{
    /// <summary>Bislang unterstützt: <c>github</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "github";

    /// <summary>Bei <c>kind=github</c>: <c>Owner/Repo</c>-Slug (z.B. <c>Kroste/ModManager.Plugins.LS25</c>).</summary>
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;
}
