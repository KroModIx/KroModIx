using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Services.Ai;

/// <summary>
/// Erkennt GPU-Hardware für die Ollama-Modell-Empfehlungen. Cross-platform:
/// <list type="bullet">
/// <item><b>Windows/Linux mit NVIDIA</b>: <c>nvidia-smi --query-gpu=name,memory.total</c></item>
/// <item><b>Windows ohne NVIDIA</b>: <c>wmic path win32_VideoController</c> (AMD/Intel Fallback)</item>
/// <item><b>Linux ohne NVIDIA</b>: <c>lspci</c> + <c>glxinfo</c>-Heuristik (best-effort — meist AMD)</item>
/// <item><b>macOS</b>: <c>system_profiler SPDisplaysDataType</c></item>
/// </list>
///
/// <para>Ohne Detektion (kein GPU-Tool im PATH, keine GPU) → <see cref="GpuInfo.Unknown"/>
/// zurück, damit die UI trotzdem Modell-Empfehlungen zeigen kann (dann CPU-Only-Set).</para>
///
/// <para>Cache: das Ergebnis wird beim ersten Aufruf ermittelt und für die Lebenszeit
/// des Services gehalten — Hardware ändert sich nicht zur Laufzeit.</para>
/// </summary>
public sealed class SystemHardwareService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private GpuInfo? _cached;
    private readonly object _lock = new();

    /// <summary>Best-effort GPU-Erkennung. Blockiert bis zum Timeout (2s pro Tool)
    /// beim ersten Aufruf, danach aus dem Cache. Bei Fehlern niemals werfen —
    /// die App muss ohne KI-Empfehlungen weiterlaufen.</summary>
    public async Task<GpuInfo> GetGpuAsync()
    {
        if (_cached is not null) return _cached;

        var info = await DetectAsync().ConfigureAwait(false);
        lock (_lock) _cached = info;
        return info;
    }

    private static async Task<GpuInfo> DetectAsync()
    {
        try
        {
            var nvidia = await TryNvidiaSmiAsync().ConfigureAwait(false);
            if (nvidia is not null) return nvidia;

            if (OperatingSystem.IsWindows())
            {
                var win = await TryWindowsWmiAsync().ConfigureAwait(false);
                if (win is not null) return win;
            }
            else if (OperatingSystem.IsMacOS())
            {
                var mac = await TryMacSystemProfilerAsync().ConfigureAwait(false);
                if (mac is not null) return mac;
            }
            else if (OperatingSystem.IsLinux())
            {
                var linux = await TryLinuxLspciAsync().ConfigureAwait(false);
                if (linux is not null) return linux;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "GPU-Detection warf — falle auf Unknown zurück.");
        }
        return GpuInfo.Unknown;
    }

    private static async Task<GpuInfo?> TryNvidiaSmiAsync()
    {
        var output = await RunAsync("nvidia-smi",
            "--query-gpu=name,memory.total", "--format=csv,noheader,nounits").ConfigureAwait(false);
        if (output is not null)
        {
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (line is not null)
            {
                var parts = line.Split(',', 2);
                if (parts.Length == 2 &&
                    int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mib))
                    return new GpuInfo(GpuVendor.Nvidia, parts[0].Trim(), Math.Round(mib / 1024.0, 1));
            }
        }

        // Fallback ohne nvidia-smi im PATH (Bazzite Atomic, Distrobox, minimale Container):
        // /proc/driver/nvidia/gpus/<busid>/information hat den Modell-Namen — VRAM
        // müssen wir aus einer kuratierten Lookup-Tabelle ableiten.
        return await TryNvidiaProcAsync().ConfigureAwait(false);
    }

    private static Task<GpuInfo?> TryNvidiaProcAsync() => Task.Run<GpuInfo?>(() =>
    {
        try
        {
            const string root = "/proc/driver/nvidia/gpus/";
            if (!Directory.Exists(root)) return null;
            foreach (var gpuDir in Directory.EnumerateDirectories(root))
            {
                var infoFile = Path.Combine(gpuDir, "information");
                if (!File.Exists(infoFile)) continue;
                var text = File.ReadAllText(infoFile);
                var m = Regex.Match(text, @"^Model:\s*(.+)$", RegexOptions.Multiline);
                if (!m.Success) continue;
                var name = m.Groups[1].Value.Trim();
                var vram = LookupNvidiaVram(name);
                return new GpuInfo(GpuVendor.Nvidia, name, vram);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Nvidia /proc Fallback fehlgeschlagen"); }
        return null;
    });

    /// <summary>Kuratierte VRAM-Werte für gängige NVIDIA-GPUs. Wird nur genutzt
    /// wenn <c>nvidia-smi</c> nicht im PATH ist und wir den Modell-Namen aus
    /// <c>/proc/driver/nvidia/gpus/*/information</c> holen. Bei Unbekannt: 0
    /// (Auto-Detect gibt „Unknown", UI zeigt Manual-Override).</summary>
    internal static double LookupNvidiaVram(string modelName)
    {
        var n = modelName.ToLowerInvariant().Replace("nvidia", "").Replace("geforce", "").Trim();
        // Reihenfolge: spezifischer zuerst (SUPER/Ti/Ti Super) damit Substring-Match nicht falsch matcht
        var table = new (string Contains, double VramGb)[]
        {
            ("rtx 5090", 32), ("rtx 5080", 16), ("rtx 5070 ti", 16), ("rtx 5070", 12), ("rtx 5060", 8),
            ("rtx 4090", 24), ("rtx 4080 super", 16), ("rtx 4080", 16), ("rtx 4070 ti super", 16),
            ("rtx 4070 super", 12), ("rtx 4070 ti", 12), ("rtx 4070", 12), ("rtx 4060 ti", 8), ("rtx 4060", 8),
            ("rtx 3090 ti", 24), ("rtx 3090", 24), ("rtx 3080 ti", 12), ("rtx 3080", 10),
            ("rtx 3070 ti", 8), ("rtx 3070", 8), ("rtx 3060 ti", 8), ("rtx 3060", 12), ("rtx 3050", 8),
            ("rtx 2080 ti", 11), ("rtx 2080 super", 8), ("rtx 2080", 8), ("rtx 2070 super", 8), ("rtx 2070", 8), ("rtx 2060", 6),
            ("rtx a6000", 48), ("rtx a5000", 24), ("rtx a4000", 16),
            ("gtx 1080 ti", 11), ("gtx 1080", 8), ("gtx 1070", 8), ("gtx 1060 6gb", 6), ("gtx 1060", 3), ("gtx 1050 ti", 4), ("gtx 1050", 2),
        };
        foreach (var (contains, vram) in table)
            if (n.Contains(contains)) return vram;
        return 0;
    }

    private static async Task<GpuInfo?> TryWindowsWmiAsync()
    {
        // wmic ist deprecated aber existiert noch. PowerShell-Alternative:
        // Get-CimInstance Win32_VideoController — braucht kein path=. Aber wmic
        // ist schneller zu parsen. Bei fehlendem wmic → CIM als Fallback.
        var wmic = await RunAsync("wmic",
            "path", "win32_VideoController", "get", "Name,AdapterRAM", "/format:csv")
            .ConfigureAwait(false);
        if (wmic is not null)
        {
            var parsed = ParseWindowsWmicCsv(wmic);
            if (parsed is not null) return parsed;
        }

        var ps = await RunAsync("powershell", "-NoProfile", "-Command",
            "Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM | ConvertTo-Json -Compress")
            .ConfigureAwait(false);
        if (ps is not null)
        {
            var parsed = ParseWindowsPowerShellJson(ps);
            if (parsed is not null) return parsed;
        }
        return null;
    }

    internal static GpuInfo? ParseWindowsWmicCsv(string csv)
    {
        // wmic csv: Node,AdapterRAM,Name — Header + Zeilen. Größte AdapterRAM gewinnt.
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("Node,", StringComparison.OrdinalIgnoreCase))
            .ToList();
        (long BytesRam, string Name)? best = null;
        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)) continue;
            var name = string.Join(',', parts.Skip(2)).Trim();
            if (best is null || bytes > best.Value.BytesRam) best = (bytes, name);
        }
        if (best is null || best.Value.BytesRam <= 0) return null;
        var gb = Math.Round(best.Value.BytesRam / 1024.0 / 1024.0 / 1024.0, 1);
        return new GpuInfo(VendorFromName(best.Value.Name), best.Value.Name, gb);
    }

    internal static GpuInfo? ParseWindowsPowerShellJson(string json)
    {
        // Kann Array oder Objekt sein. Wir nehmen das größte AdapterRAM.
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<System.Text.Json.JsonElement> { doc.RootElement };
            (long BytesRam, string Name)? best = null;
            foreach (var it in items)
            {
                if (!it.TryGetProperty("AdapterRAM", out var ramEl)) continue;
                if (!it.TryGetProperty("Name", out var nameEl)) continue;
                long bytes = ramEl.ValueKind == System.Text.Json.JsonValueKind.Number ? ramEl.GetInt64() : 0;
                var name = nameEl.GetString() ?? "";
                if (best is null || bytes > best.Value.BytesRam) best = (bytes, name);
            }
            if (best is null || best.Value.BytesRam <= 0) return null;
            var gb = Math.Round(best.Value.BytesRam / 1024.0 / 1024.0 / 1024.0, 1);
            return new GpuInfo(VendorFromName(best.Value.Name), best.Value.Name, gb);
        }
        catch { return null; }
    }

    private static async Task<GpuInfo?> TryLinuxLspciAsync()
    {
        // lspci -mm -nn: Vendor + Name in einer Zeile. VRAM kriegen wir hier nicht direkt.
        // Für AMD: /sys/class/drm/card*/device/mem_info_vram_total (KMS-Amdgpu). Für Intel
        // integrated: kein sinnvoller VRAM-Wert (shared memory). Best-effort:
        var lspci = await RunAsync("lspci", "-mm", "-nn").ConfigureAwait(false);
        if (lspci is null) return null;
        var line = lspci.Split('\n')
            .FirstOrDefault(l => l.Contains("VGA", StringComparison.OrdinalIgnoreCase)
                              || l.Contains("3D controller", StringComparison.OrdinalIgnoreCase)
                              || l.Contains("Display controller", StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;

        var name = ExtractLspciName(line);
        var vendor = VendorFromName(name);
        double vramGb = 0;
        try
        {
            foreach (var vramFile in Directory.GetFiles("/sys/class/drm/", "mem_info_vram_total", SearchOption.AllDirectories))
            {
                if (long.TryParse(File.ReadAllText(vramFile).Trim(), out var bytes))
                {
                    var gb = Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
                    if (gb > vramGb) vramGb = gb;
                }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "AMD VRAM-Read fehlgeschlagen"); }

        return new GpuInfo(vendor, name, vramGb);
    }

    internal static string ExtractLspciName(string lspciLine)
    {
        // Format: `01:00.0 "class-desc [0300]" "vendor [10de]" "device [2704]" -rXX`
        // Drei quoted Felder: [0]=class, [1]=vendor, [2]=device.
        var matches = Regex.Matches(lspciLine, "\"([^\"]*)\"");
        if (matches.Count < 3) return lspciLine.Trim();
        var vendor = matches[1].Groups[1].Value;
        var device = matches[2].Groups[1].Value;
        vendor = Regex.Replace(vendor, @"\s*\[[0-9a-f]{4}\]\s*$", "");
        device = Regex.Replace(device, @"\s*\[[0-9a-f]{4}\]\s*$", "");
        return $"{vendor} {device}".Trim();
    }

    private static async Task<GpuInfo?> TryMacSystemProfilerAsync()
    {
        var output = await RunAsync("system_profiler", "SPDisplaysDataType").ConfigureAwait(false);
        if (output is null) return null;

        var chipset = Regex.Match(output, @"Chipset Model:\s*(.+)");
        var vramMatch = Regex.Match(output, @"VRAM \([^)]+\):\s*(\d+)\s*(MB|GB)");
        if (!chipset.Success) return null;

        var name = chipset.Groups[1].Value.Trim();
        double vramGb = 0;
        if (vramMatch.Success && double.TryParse(vramMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            vramGb = vramMatch.Groups[2].Value.Equals("GB", StringComparison.OrdinalIgnoreCase) ? num : Math.Round(num / 1024.0, 1);

        return new GpuInfo(VendorFromName(name), name, vramGb);
    }

    internal static GpuVendor VendorFromName(string name)
    {
        var lo = name.ToLowerInvariant();
        if (lo.Contains("nvidia") || lo.Contains("geforce") || lo.Contains("rtx") || lo.Contains("quadro") || lo.Contains("tesla"))
            return GpuVendor.Nvidia;
        if (lo.Contains("amd") || lo.Contains("radeon") || lo.Contains("navi"))
            return GpuVendor.Amd;
        if (lo.Contains("intel") || lo.Contains("iris") || lo.Contains("uhd graphics") || lo.Contains("hd graphics"))
            return GpuVendor.Intel;
        if (lo.Contains("apple m"))
            return GpuVendor.AppleSilicon;
        return GpuVendor.Unknown;
    }

    private static async Task<string?> RunAsync(string command, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { /* nothing */ }
                return null;
            }
            if (proc.ExitCode != 0) return null;
            return await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Tool nicht installiert
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "GPU-Tool '{Cmd}' warf", command);
            return null;
        }
    }
}

/// <summary>Ergebnis der Hardware-Erkennung. <see cref="VramGb"/> = 0 bedeutet
/// „unbekannt" (Integrated Graphics ohne dediziertes VRAM oder Tool nicht
/// verfügbar) — die UI muss dann eine CPU-Only-Empfehlung zeigen.</summary>
public sealed record GpuInfo(GpuVendor Vendor, string Name, double VramGb)
{
    public static GpuInfo Unknown => new(GpuVendor.Unknown, "Unbekannt", 0);
    public bool HasDedicatedVram => VramGb >= 1.0;
}

public enum GpuVendor { Unknown, Nvidia, Amd, Intel, AppleSilicon }
