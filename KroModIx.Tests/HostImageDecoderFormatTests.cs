#pragma warning disable xUnit1051
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Services.Images;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Pure Format-Detection Tests — brauchen kein Avalonia-Runtime,
/// laufen daher schnell und ohne Headless-Fixture. Deckt alle unterstuetzten
/// Formate + Edge-Cases (null, empty, corrupt) ab.</summary>
public class HostImageDecoderFormatTests
{
    // Minimal 1x1 red PNG (67 Bytes)
    public static readonly byte[] TinyPng = new byte[]
    {
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,
        0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
        0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,0xDE,0x00,0x00,0x00,
        0x0C,0x49,0x44,0x41,0x54,0x08,0x99,0x63,0xF8,0xCF,0xC0,0x00,
        0x00,0x00,0x03,0x00,0x01,0x5B,0xE8,0x0A,0xF0,0x00,0x00,0x00,
        0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82,
    };

    public static readonly byte[] JpegMagic = new byte[] { 0xFF,0xD8,0xFF,0xE0,0,0,0,0,0,0,0,0 };
    public static readonly byte[] GifMagic = new byte[] { 0x47,0x49,0x46,0x38,0x39,0x61,0,0,0,0,0,0 };
    public static readonly byte[] BmpMagic = new byte[] { 0x42,0x4D,0,0,0,0,0,0,0,0,0,0 };
    public static readonly byte[] WebPMagic = new byte[]
    {
        0x52,0x49,0x46,0x46, 0x1A,0x00,0x00,0x00, 0x57,0x45,0x42,0x50,
        0x56,0x50,0x38,0x4C,
    };
    public static readonly byte[] AvifMagic = new byte[]
    {
        0x00,0x00,0x00,0x20, 0x66,0x74,0x79,0x70, 0x61,0x76,0x69,0x66,
    };
    public static readonly byte[] AvifAvisMagic = new byte[]
    {
        0x00,0x00,0x00,0x20, 0x66,0x74,0x79,0x70, 0x61,0x76,0x69,0x73,
    };
    public static readonly byte[] HeicMagic = new byte[]
    {
        0x00,0x00,0x00,0x20, 0x66,0x74,0x79,0x70, 0x68,0x65,0x69,0x63,
    };
    public static readonly byte[] HeicHeixMagic = new byte[]
    {
        0x00,0x00,0x00,0x20, 0x66,0x74,0x79,0x70, 0x68,0x65,0x69,0x78,
    };
    public static readonly byte[] HeicMif1Magic = new byte[]
    {
        0x00,0x00,0x00,0x20, 0x66,0x74,0x79,0x70, 0x6D,0x69,0x66,0x31,
    };
    public static readonly byte[] DdsMagic = new byte[]
    {
        0x44,0x44,0x53,0x20, 0x7C,0x00,0x00,0x00,
    };

    // ---- Static Format-Detection ----

    [Fact] public void DetectFormat_Png() =>
        HostImageDecoderImpl.DetectFormat(TinyPng).Should().Be(ImageFormat.Png);

    [Fact] public void DetectFormat_Jpeg() =>
        HostImageDecoderImpl.DetectFormat(JpegMagic).Should().Be(ImageFormat.Jpeg);

    [Fact] public void DetectFormat_Gif() =>
        HostImageDecoderImpl.DetectFormat(GifMagic).Should().Be(ImageFormat.Gif);

    [Fact] public void DetectFormat_Bmp() =>
        HostImageDecoderImpl.DetectFormat(BmpMagic).Should().Be(ImageFormat.Bmp);

    [Fact] public void DetectFormat_WebP() =>
        HostImageDecoderImpl.DetectFormat(WebPMagic).Should().Be(ImageFormat.WebP);

    [Fact] public void DetectFormat_Avif_Avif() =>
        HostImageDecoderImpl.DetectFormat(AvifMagic).Should().Be(ImageFormat.Avif);

    [Fact] public void DetectFormat_Avif_Avis() =>
        HostImageDecoderImpl.DetectFormat(AvifAvisMagic).Should().Be(ImageFormat.Avif);

    [Fact] public void DetectFormat_Heic_Heic() =>
        HostImageDecoderImpl.DetectFormat(HeicMagic).Should().Be(ImageFormat.Heic);

    [Fact] public void DetectFormat_Heic_Heix() =>
        HostImageDecoderImpl.DetectFormat(HeicHeixMagic).Should().Be(ImageFormat.Heic);

    [Fact] public void DetectFormat_Heic_Mif1() =>
        HostImageDecoderImpl.DetectFormat(HeicMif1Magic).Should().Be(ImageFormat.Heic);

    [Fact] public void DetectFormat_Dds() =>
        HostImageDecoderImpl.DetectFormat(DdsMagic).Should().Be(ImageFormat.Dds);

    [Fact] public void DetectFormat_UnknownBytes() =>
        HostImageDecoderImpl.DetectFormat(new byte[] { 0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C })
            .Should().Be(ImageFormat.Unknown);

    [Fact] public void DetectFormat_Empty() =>
        HostImageDecoderImpl.DetectFormat(new byte[0]).Should().Be(ImageFormat.Unknown);

    [Fact] public void DetectFormat_TooShortFor4Bytes() =>
        HostImageDecoderImpl.DetectFormat(new byte[] { 0x89, 0x50, 0x4E }).Should().Be(ImageFormat.Unknown);

    [Fact] public void DetectFormat_TooShortForWebP() =>
        HostImageDecoderImpl.DetectFormat(new byte[] { 0x52,0x49,0x46,0x46,0,0,0,0 })
            .Should().Be(ImageFormat.Unknown);

    [Fact] public void DetectFormat_Null() =>
        HostImageDecoderImpl.DetectFormat(null!).Should().Be(ImageFormat.Unknown);

    [Fact]
    public void DetectFormat_HtmlLoginWall_IsNotImage()
    {
        var html = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Login required</body></html>");
        HostImageDecoderImpl.DetectFormat(html).Should().Be(ImageFormat.Unknown);
    }

    [Fact]
    public void DetectFormat_JsonError_IsNotImage()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"error\":\"401 Unauthorized\"}");
        HostImageDecoderImpl.DetectFormat(json).Should().Be(ImageFormat.Unknown);
    }

    // ---- LooksLikeImage ----

    [Fact]
    public void LooksLikeImage_TrueForKnownFormats()
    {
        var d = new HostImageDecoderImpl();
        d.LooksLikeImage(TinyPng).Should().BeTrue();
        d.LooksLikeImage(JpegMagic).Should().BeTrue();
        d.LooksLikeImage(WebPMagic).Should().BeTrue();
        d.LooksLikeImage(DdsMagic).Should().BeTrue();
        d.LooksLikeImage(AvifMagic).Should().BeTrue();
        d.LooksLikeImage(HeicMagic).Should().BeTrue();
        d.LooksLikeImage(GifMagic).Should().BeTrue();
        d.LooksLikeImage(BmpMagic).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeImage_FalseForGarbage()
    {
        var d = new HostImageDecoderImpl();
        d.LooksLikeImage(new byte[] { 0, 0, 0, 0, 0 }).Should().BeFalse();
        d.LooksLikeImage(new byte[0]).Should().BeFalse();
        d.LooksLikeImage(null!).Should().BeFalse();
        d.LooksLikeImage(System.Text.Encoding.UTF8.GetBytes("garbage html <html>")).Should().BeFalse();
    }

    // ---- Empty/Null Input (no Avalonia needed — early returns) ----

    [Fact]
    public async Task DecodeAsync_NullBytes_ReturnsNull()
    {
        var d = new HostImageDecoderImpl();
        (await d.DecodeAsync((byte[])null!)).Should().BeNull();
    }

    [Fact]
    public async Task DecodeAsync_EmptyBytes_ReturnsNull()
    {
        var d = new HostImageDecoderImpl();
        (await d.DecodeAsync(System.Array.Empty<byte>())).Should().BeNull();
    }

    [Fact]
    public async Task DecodeAsync_UnknownFormat_ReturnsNull()
    {
        var d = new HostImageDecoderImpl();
        (await d.DecodeAsync(new byte[] { 0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C })).Should().BeNull();
    }

    [Fact]
    public async Task DecodeAsync_NullStream_ReturnsNull()
    {
        var d = new HostImageDecoderImpl();
        (await d.DecodeAsync((System.IO.Stream)null!)).Should().BeNull();
    }

    [Fact]
    public async Task DecodeFileAsync_NonexistentFile_ReturnsNull()
    {
        var d = new HostImageDecoderImpl();
        (await d.DecodeFileAsync("/tmp/nonexistent-" + System.Guid.NewGuid().ToString("N") + ".png")).Should().BeNull();
    }

    [Fact]
    public async Task DecodeFileAsync_EmptyPath_ReturnsNull()
    {
        var d = new HostImageDecoderImpl();
        (await d.DecodeFileAsync("")).Should().BeNull();
        (await d.DecodeFileAsync(null!)).Should().BeNull();
    }
}
