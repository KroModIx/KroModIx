using System;

namespace KroModIx.Plugin.Contracts;

/// <summary>Wird geworfen, wenn ein Plugin einen Host-Baukasten benutzt, den
/// dieser Host nicht mitbringt.
///
/// <para><b>Warum das wirft und nicht still nichts tut.</b> Die älteren
/// Baukästen (<see cref="IBackupService"/>,
/// <see cref="IDescriptionParser"/>, …) haben Null-Implementierungen, die
/// folgenlos nichts tun — ein ausgefallener Snapshot oder ein nicht
/// aufgelöstes BBCode ist unschön, aber harmlos.</para>
///
/// <para>Bei den Baukästen, die <b>Dateien im Spiel verändern</b>
/// (<see cref="IArchiveService"/>, <see cref="IUnrealPakService"/>,
/// <see cref="IWinePrefixService"/>) wäre dieselbe Nachsicht gefährlich:
/// ein folgenlos „erfolgreicher" Install hätte nichts installiert, und der
/// User sucht den Fehler im Spiel statt an der Host-Version. Lesende
/// Abfragen dieser Baukästen antworten deshalb neutral (leer, false,
/// <c>Unknown</c>), aber jede <b>Aktion</b> scheitert laut mit dieser
/// Ausnahme.</para>
///
/// <para>Im Normalfall tritt sie nie auf: ein Plugin pinnt die benötigte
/// Host-Version über <c>minHostVersion</c> in seiner <c>plugin.json</c>, und
/// der Host lädt es bei einer älteren Version gar nicht erst.</para></summary>
public sealed class HostFeatureUnavailableException : Exception
{
    public HostFeatureUnavailableException(string feature, string minHostVersion)
        : base($"„{feature}“ braucht KroModIx ab v{minHostVersion}. " +
               $"Dieser Host ist älter — bitte aktualisieren, oder " +
               $"minHostVersion in der plugin.json des Plugins prüfen.")
    {
        Feature = feature;
        MinHostVersion = minHostVersion;
    }

    /// <summary>Der Baukasten, der fehlt — für Log-Auswertung.</summary>
    public string Feature { get; }

    /// <summary>Ab welcher Host-Version es ihn gibt.</summary>
    public string MinHostVersion { get; }
}
