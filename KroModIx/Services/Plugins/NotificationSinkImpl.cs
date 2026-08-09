using System;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Services.Plugins;

/// <summary>Sammelt Plugin-Notifications; MainWindow abonniert und rendert
/// sie im Statusbar (M2: einfacher Text; toast-artige UI kommt später).</summary>
public sealed class NotificationSinkImpl : INotificationSink
{
    public event EventHandler<NotificationEventArgs>? Notified;

    public void Notify(string message, NotificationLevel level = NotificationLevel.Info)
        => Notified?.Invoke(this, new NotificationEventArgs(message, level));
}

public sealed record NotificationEventArgs(string Message, NotificationLevel Level);
