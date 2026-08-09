using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace KroModIx.Localization;

/// <summary>
/// Bindbarer Wrapper um einen Localization-Key. Statisch pro Key gecacht — sonst
/// wird der Wrapper vom GC eingesammelt, weil Avalonia <c>Binding.Source</c> nicht
/// dauerhaft stark hält, und der Live-Sprachwechsel greift nur im aktiven Fenster.
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    public string Key { get; }
    public string Value => LocalizationService.Instance[Key];

    private static readonly Dictionary<string, LocalizedString> Cache = new(StringComparer.Ordinal);
    private static readonly object Lock = new();

    private LocalizedString(string key) => Key = key;

    public static LocalizedString Get(string key)
    {
        lock (Lock)
        {
            if (!Cache.TryGetValue(key, out var s))
            {
                s = new LocalizedString(key);
                Cache[key] = s;
            }
            return s;
        }
    }

    internal static void NotifyAllChanged()
    {
        LocalizedString[] snapshot;
        lock (Lock) snapshot = Cache.Values.ToArray();
        foreach (var s in snapshot) s.OnPropertyChanged(nameof(Value));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
