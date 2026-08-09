using System;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Der Fortschritts-Status, den der Host-Statusbar rendert. Nur eine Instanz
/// gleichzeitig — falls mehrere Plugins parallel Progress reporten, „gewinnt"
/// der zuletzt gestartete (M2 Vereinfachung; sauber wird das ab M4/M5).
/// </summary>
public sealed class StatusProgressCoordinator
{
    private StatusProgress? _current;

    public event EventHandler<StatusProgressChangedEventArgs>? Changed;

    public IProgressScope Begin(string title)
    {
        _current = new StatusProgress(title, this);
        RaiseChanged();
        return _current;
    }

    internal void EndScope(StatusProgress scope)
    {
        if (ReferenceEquals(_current, scope))
        {
            _current = null;
            RaiseChanged();
        }
    }

    internal void ReportChanged() => RaiseChanged();

    private void RaiseChanged()
    {
        var snap = _current;
        Changed?.Invoke(this, new StatusProgressChangedEventArgs(
            IsActive: snap is not null,
            Title: snap?.Title,
            Message: snap?.Message,
            Fraction: snap?.Fraction,
            Indeterminate: snap?.Indeterminate ?? false));
    }
}

public sealed record StatusProgressChangedEventArgs(
    bool IsActive, string? Title, string? Message, double? Fraction, bool Indeterminate);

internal sealed class StatusProgress : IProgressScope
{
    private readonly StatusProgressCoordinator _coord;
    private bool _disposed;

    public string Title { get; }
    public string? Message { get; private set; }
    public double? Fraction { get; private set; }
    public bool Indeterminate { get; private set; }

    public StatusProgress(string title, StatusProgressCoordinator coord)
    {
        Title = title;
        _coord = coord;
    }

    public void Report(double fraction, string? message = null)
    {
        if (_disposed) return;
        Fraction = Math.Clamp(fraction, 0.0, 1.0);
        Indeterminate = false;
        if (message is not null) Message = message;
        _coord.ReportChanged();
    }

    public void SetIndeterminate(string? message = null)
    {
        if (_disposed) return;
        Indeterminate = true;
        Fraction = null;
        if (message is not null) Message = message;
        _coord.ReportChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coord.EndScope(this);
    }
}
