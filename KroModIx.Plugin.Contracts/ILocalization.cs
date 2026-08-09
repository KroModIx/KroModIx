using System;

namespace KroModIx.Plugin.Contracts;

/// <summary>Erlaubt Plugins, sich beim Sprachwechsel des Hosts zu abonnieren.
/// Plugin-eigene Übersetzungen liegen im Plugin-Ordner (z.B. eigene .resx),
/// werden aber am gleichen Kultur-Zustand ausgerichtet.</summary>
public interface ILocalization
{
    /// <summary>Aktueller ISO-Code (z.B. <c>de</c>, <c>en</c>).</summary>
    string CurrentIso { get; }

    /// <summary>Feuert, wenn der User die Sprache im Host wechselt.</summary>
    event EventHandler? CurrentChanged;
}
