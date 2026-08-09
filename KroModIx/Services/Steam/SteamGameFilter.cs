using System;
using System.Collections.Generic;

namespace KroModIx.Services.Steam;

/// <summary>
/// Blendet Steam-eigene Tools (Runtimes, Proton-Versionen, Redistributables)
/// aus der Sidebar aus — der Nutzer sieht so nur „richtige" Spiele.
/// Filter greift zweigleisig: bekannte AppIds als Blacklist + Namens-Präfixe
/// als Fallback (schützt vor neuen Proton-/Runtime-Versionen, deren AppId wir
/// noch nicht kennen).
/// </summary>
public static class SteamGameFilter
{
    /// <summary>Bekannte Steam-Tool-AppIds (Runtimes, Proton-Builds, Redistributables).
    /// Namens-Präfixe (siehe <see cref="LooksLikeTool"/>) fangen weitere ab.</summary>
    public static readonly HashSet<int> KnownToolAppIds = new()
    {
        228980,  // Steamworks Common Redistributables
        1070560, // Steam Linux Runtime 1.0 (scout)
        1391110, // Steam Linux Runtime 2.0 (soldier)
        1628350, // Steam Linux Runtime 3.0 (sniper)
        // Proton-Versionen (bekannte, historische + aktuelle)
        858280,  // Proton 4.11
        961940,  // Proton 5.0
        1054830, // Proton 5.13
        1113280, // Proton 4.2
        1245040, // Proton 5.13-Update
        1420170, // Proton 6.3
        1493710, // Proton Experimental
        1580130, // Proton 7.0
        1887720, // Proton 7.0-Update
        2180100, // Proton Hotfix
        2230260, // Proton 8.0
        2348590, // Proton 9.0
        2805730, // Proton 9.0-Update
        3164500, // Proton 10.0 (vermutlich)
    };

    public static bool IsTool(int appId, string name)
    {
        if (KnownToolAppIds.Contains(appId)) return true;
        return LooksLikeTool(name);
    }

    private static bool LooksLikeTool(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // Namens-Präfixe für Steam-eigene Tools. Case-insensitive damit Umbenennungen
        // (Steam wechselt zwischen "Proton" und "Proton Experimental") nicht durchrutschen.
        return
            name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Proton ", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Proton", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Proton Experimental", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Proton Hotfix", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase);
    }
}
