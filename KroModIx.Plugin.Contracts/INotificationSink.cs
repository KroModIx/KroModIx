namespace KroModIx.Plugin.Contracts;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Nicht-modale Benachrichtigung (Toast im Statusbar). Plugins nutzen
/// das für „N Mods installiert", „Download fertig", „Verbindung verloren" etc.</summary>
public interface INotificationSink
{
    void Notify(string message, NotificationLevel level = NotificationLevel.Info);
}
