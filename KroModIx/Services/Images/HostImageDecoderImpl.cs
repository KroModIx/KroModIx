using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Images;

/// <summary>Host-Impl von <see cref="IImageDecoder"/> mit Format-Chain.
/// <para>Fluss pro Decode:</para>
/// <list type="number">
/// <item>Magic-Byte-Detection erkennt Format.</item>
/// <item>Fuer nativ von Avalonia unterstuetzte Formate (PNG/JPEG/BMP/GIF):
///   direktes <c>new Bitmap(stream)</c> auf UI-Thread.</item>
/// <item>Fuer WebP/AVIF/DDS: ffmpeg-Convert zu PNG in Temp-File, dann
///   Bitmap-Ctor mit PNG. Bei fehlendem ffmpeg im PATH: silent null.</item>
/// <item>Unbekannte Bytes: null (Consumer zeigt Fallback-Icon).</item>
/// </list>
///
/// <para><b>Thread-Model:</b> Alle Public-Methods sind thread-safe. Bitmap-
/// Instantiation immer auf UI-Thread via <see cref="Dispatcher.UIThread"/>
/// weil Skia teilweise Thread-Affinity hat. ffmpeg-Convert laeuft off-thread.</para></summary>
public sealed class HostImageDecoderImpl : IImageDecoder
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public async Task<Bitmap?> DecodeAsync(byte[] bytes, CancellationToken ct = default)
    {
        if (bytes is null || bytes.Length == 0) return null;

        var format = DetectFormat(bytes);
        Log.Debug("DecodeAsync: {N} Bytes, Format={Fmt}", bytes.Length, format);

        return format switch
        {
            ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Bmp or ImageFormat.Gif
                => await LoadNativeAsync(bytes, ct).ConfigureAwait(false),
            ImageFormat.WebP or ImageFormat.Avif or ImageFormat.Dds or ImageFormat.Heic
                => await ConvertAndLoadAsync(bytes, format, ct).ConfigureAwait(false),
            _ => null,
        };
    }

    public async Task<Bitmap?> DecodeAsync(Stream stream, CancellationToken ct = default)
    {
        if (stream is null) return null;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return await DecodeAsync(ms.ToArray(), ct).ConfigureAwait(false);
    }

    public async Task<Bitmap?> DecodeFileAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return await DecodeAsync(bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "DecodeFileAsync fehlgeschlagen: {Path}", path);
            return null;
        }
    }

    public bool LooksLikeImage(byte[] bytes) => DetectFormat(bytes) != ImageFormat.Unknown;

    // ---- Native-Decode ----

    private static async Task<Bitmap?> LoadNativeAsync(byte[] bytes, CancellationToken ct)
    {
        try
        {
            Bitmap? bmp = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(bytes);
                bmp = new Bitmap(ms);
            });
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Native Bitmap-Ctor fehlgeschlagen ({N} Bytes)", bytes.Length);
            return null;
        }
    }

    // ---- ffmpeg-Convert-Chain ----

    private static async Task<Bitmap?> ConvertAndLoadAsync(byte[] bytes, ImageFormat format, CancellationToken ct)
    {
        var ext = format switch
        {
            ImageFormat.WebP => ".webp",
            ImageFormat.Avif => ".avif",
            ImageFormat.Dds => ".dds",
            ImageFormat.Heic => ".heic",
            _ => ".bin",
        };
        var pngBytes = await TryConvertWithFfmpegAsync(bytes, ext, ct).ConfigureAwait(false);
        if (pngBytes is null) return null;
        return await LoadNativeAsync(pngBytes, ct).ConfigureAwait(false);
    }

    /// <summary>Ruft ffmpeg als Subprocess. Umweg ueber Temp-Files (statt
    /// stdin/stdout-Pipe) weil letztere im .NET-Pipe-Wrapper partial-write-
    /// Bugs bei ~300 KB Files hatte (siehe RenPyAssist CoverCache-Muster).
    /// Bei fehlendem ffmpeg im PATH: return null, kein Crash.</summary>
    private static async Task<byte[]?> TryConvertWithFfmpegAsync(byte[] source, string inputExt, CancellationToken ct)
    {
        var inPath = Path.Combine(Path.GetTempPath(), $"kromodix-img-{Guid.NewGuid():N}{inputExt}");
        var outPath = Path.Combine(Path.GetTempPath(), $"kromodix-img-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(inPath, source, ct).ConfigureAwait(false);
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-y", "-nostdin", "-loglevel", "error",
                "-i", inPath, "-c:v", "png", outPath })
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0 || !File.Exists(outPath))
            {
                Log.Debug("ffmpeg Image-Convert ({Ext}) exit={Code}, stderr={Err}",
                    inputExt, proc.ExitCode, await errTask);
                return null;
            }
            return await File.ReadAllBytesAsync(outPath, ct).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log.Debug("ffmpeg nicht installiert — {Ext}-Convert nicht moeglich", inputExt);
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ffmpeg-Convert Ausnahme");
            return null;
        }
        finally
        {
            try { if (File.Exists(inPath)) File.Delete(inPath); } catch { }
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
        }
    }

    // ---- Format-Detection ----

    public static ImageFormat DetectFormat(byte[] b)
    {
        if (b is null || b.Length < 4) return ImageFormat.Unknown;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ImageFormat.Png;
        // JPEG: FF D8 FF
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ImageFormat.Jpeg;
        // BMP: 42 4D
        if (b[0] == 0x42 && b[1] == 0x4D) return ImageFormat.Bmp;
        // GIF: 47 49 46 38 (GIF8)
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return ImageFormat.Gif;
        // DDS: 44 44 53 20 (DDS ' ')
        if (b[0] == 0x44 && b[1] == 0x44 && b[2] == 0x53 && b[3] == 0x20) return ImageFormat.Dds;

        // Container-basierte Formate brauchen mindestens 12 Bytes
        if (b.Length < 12) return ImageFormat.Unknown;

        // WebP: RIFF....WEBP (Bytes 0-3=RIFF, 8-11=WEBP)
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
            return ImageFormat.WebP;

        // AVIF: ....ftyp....avif (Bytes 4-7=ftyp, 8-11=avif oder avis)
        if (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70)
        {
            if (b[8] == 0x61 && b[9] == 0x76 && b[10] == 0x69 && b[11] == 0x66) return ImageFormat.Avif;
            if (b[8] == 0x61 && b[9] == 0x76 && b[10] == 0x69 && b[11] == 0x73) return ImageFormat.Avif;
            // HEIC: ftypheic / ftypheix / ftypmif1
            if (b[8] == 0x68 && b[9] == 0x65 && b[10] == 0x69 && (b[11] == 0x63 || b[11] == 0x78)) return ImageFormat.Heic;
            if (b[8] == 0x6D && b[9] == 0x69 && b[10] == 0x66 && b[11] == 0x31) return ImageFormat.Heic;
        }

        return ImageFormat.Unknown;
    }
}

/// <summary>Von <see cref="HostImageDecoderImpl.DetectFormat"/> erkannte Formate.</summary>
public enum ImageFormat
{
    Unknown,
    Png,
    Jpeg,
    Bmp,
    Gif,
    WebP,
    Avif,
    Dds,
    Heic,
}
