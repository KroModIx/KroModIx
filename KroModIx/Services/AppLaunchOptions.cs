using System;
using System.Collections.Generic;
using System.Globalization;
using NLog;

namespace KroModIx.Services;

/// <summary>
/// Ergebnis des CLI-Parsers. Nur die vom Host verstandenen Args landen hier —
/// der Rest wird als <see cref="RemainingArgs"/> an <c>StartWithClassicDesktopLifetime</c>
/// durchgereicht, damit Avalonia seine eigenen Flags (<c>--framework</c> etc.)
/// weiter sehen kann.
/// </summary>
public sealed class AppLaunchOptions
{
    public int? ApiPortOverride { get; init; }
    public string? ApiTokenOverride { get; init; }
    public TimeSpan? AutoShutdownAfter { get; init; }
    public string[] RemainingArgs { get; init; } = Array.Empty<string>();

    public static AppLaunchOptions Parse(string[] args)
    {
        var log = LogManager.GetCurrentClassLogger();
        int? port = null;
        string? token = null;
        TimeSpan? shutdown = null;
        var remaining = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--api-port":
                {
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p is > 0 and < 65536)
                        port = p;
                    else
                        log.Warn("--api-port ohne gültigen Wert (1-65535) — ignoriert.");
                    break;
                }
                case "--api-token":
                {
                    if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                        token = args[++i];
                    else
                        log.Warn("--api-token ohne Wert — ignoriert.");
                    break;
                }
                case "--auto-shutdown-after":
                {
                    if (i + 1 < args.Length && TryParseDuration(args[++i], out var d))
                        shutdown = d;
                    else
                        log.Warn("--auto-shutdown-after ohne gültigen Wert (z.B. 30s, 2m, 1h) — ignoriert.");
                    break;
                }
                default:
                    remaining.Add(a);
                    break;
            }
        }

        return new AppLaunchOptions
        {
            ApiPortOverride = port,
            ApiTokenOverride = token,
            AutoShutdownAfter = shutdown,
            RemainingArgs = remaining.ToArray(),
        };
    }

    private static bool TryParseDuration(string raw, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        if (raw.Length < 2) return false;
        var suffix = raw[^1];
        var body = raw[..^1];
        if (!double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0)
            return false;
        value = suffix switch
        {
            's' or 'S' => TimeSpan.FromSeconds(n),
            'm' or 'M' => TimeSpan.FromMinutes(n),
            'h' or 'H' => TimeSpan.FromHours(n),
            _ => TimeSpan.Zero,
        };
        return value > TimeSpan.Zero;
    }
}
