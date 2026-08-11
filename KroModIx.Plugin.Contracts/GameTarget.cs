using System;

namespace KroModIx.Plugin.Contracts;

/// <summary>
/// Statische Deklaration eines Spiels, das ein Plugin bedienen kann. Wird
/// primär aus dem <see cref="PluginManifest"/> gelesen (der Host sieht das
/// Target dadurch ohne Assembly-Load) und ist zur Laufzeit als
/// <see cref="DetectedGame.Target"/> verfügbar.
/// </summary>
/// <param name="GameId">Stabiler Slug, z.B. <c>farming-simulator-25</c>. Wird
/// für Persistenz, Deep-Links und Konflikt-Auflösung benutzt.</param>
/// <param name="DisplayName">Anzeigename in der Sidebar.</param>
/// <param name="SteamAppId">Steam-App-ID, wenn das Spiel via Steam vertrieben
/// wird. Ist die primäre Auto-Discovery-Quelle.</param>
/// <param name="AlternativeExecutableNames">Dateinamen (mit Extension), die
/// bei Non-Steam-Discovery matchen. Beispiel: <c>FactoryGame.exe</c>.</param>
/// <param name="Platforms">Auf welchen OS das Plugin dieses Spiel bedient.
/// <see cref="Platforms.Both"/> ist der Regelfall.</param>
public sealed record GameTarget(
    string GameId,
    string DisplayName,
    int? SteamAppId,
    string[] AlternativeExecutableNames,
    Platforms Platforms)
{
    /// <summary>Engine-Slug für ordnerbasierte Discovery — z. B.
    /// <c>renpy</c>. Wenn gesetzt, matcht das Target JEDES Manual-Game mit
    /// derselben <c>Engine</c>-Property (der User kann pro Spiel eine eigene
    /// Kachel bekommen ohne dass jede eine Steam-AppId braucht). Contracts v1.9.0+.
    /// Init-only Property statt Ctor-Parameter, damit die 5-Positional-Args-
    /// Signatur binary-kompatibel zu Contracts &lt;=1.8.0 bleibt.</summary>
    public string? Engine { get; init; }
}

[Flags]
public enum Platforms
{
    None = 0,
    Windows = 1,
    Linux = 2,
    Both = Windows | Linux,
}
