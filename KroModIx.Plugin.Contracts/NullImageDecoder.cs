using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace KroModIx.Plugin.Contracts;

/// <summary>Default-Impl fuer Hosts &lt; v1.18.0 die die Contract-Methode
/// nicht implementieren. Versucht nur den nativen Avalonia-Bitmap-Ctor —
/// funktioniert fuer PNG/JPEG/BMP/GIF, scheitert fuer WebP/AVIF/DDS.</summary>
public sealed class NullImageDecoder : IImageDecoder
{
    public static readonly NullImageDecoder Instance = new();
    private NullImageDecoder() { }

    public async Task<Bitmap?> DecodeAsync(byte[] bytes, CancellationToken ct = default)
    {
        if (bytes is null || bytes.Length == 0) return null;
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
        catch { return null; }
    }

    public Task<Bitmap?> DecodeAsync(Stream stream, CancellationToken ct = default)
    {
        if (stream is null) return Task.FromResult<Bitmap?>(null);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return DecodeAsync(ms.ToArray(), ct);
    }

    public async Task<Bitmap?> DecodeFileAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return await DecodeAsync(bytes, ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    /// <summary>Kennt nur die 4 „nativen" Avalonia-Formate — WebP/AVIF/DDS
    /// werden als „kein Bild" gemeldet weil dieser Null-Decoder sie eh
    /// nicht dekodieren koennte.</summary>
    public bool LooksLikeImage(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 4) return false;
        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // GIF: 47 49 46 38
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true;
        // BMP: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        return false;
    }
}
