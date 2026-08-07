using System;

namespace ModManager.PluginContracts;

/// <summary>Wird von <see cref="IHostServices.BeginProgress"/> geliefert. Dispose
/// beendet den Fortschritts-Balken im Host-Statusbar. Fortschritt ist ein
/// Bruchteil 0..1.</summary>
public interface IProgressScope : IDisposable
{
    void Report(double fraction, string? message = null);

    /// <summary>Setzt den Progress auf indeterminate (Marquee-Modus).</summary>
    void SetIndeterminate(string? message = null);
}
