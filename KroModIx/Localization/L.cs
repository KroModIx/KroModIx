namespace KroModIx.Localization;

/// <summary>Kurz-Helper für ViewModels: <c>L.T("Key")</c> statt <c>LocalizationService.Instance["Key"]</c>.</summary>
internal static class L
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string F(string key, params object?[] args)
        => string.Format(LocalizationService.Instance[key], args);
}
