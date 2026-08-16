using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace KroModIx.Plugin.Contracts;

/// <summary>Zentraler Bild-Decoder-Baukasten. Nimmt beliebige Bild-Bytes/
/// -Streams/-Files und gibt eine <see cref="Bitmap"/> zurueck die Avalonia
/// direkt rendern kann — oder <c>null</c> wenn alle Decode-Wege fehlschlagen.
///
/// <para>Loest das plugin-uebergreifende Problem: Nexus liefert Cover als
/// WebP (Cyberpunk, DSP), f95zone als AVIF (RenPyAssist), GIANTS-ModHub als
/// DDS (LS25). Avalonias Bitmap-Ctor kann nur PNG/JPEG/BMP/GIF nativ —
/// alles andere braucht Convert. Statt dass jedes Plugin die Convert-Chain
/// (ffmpeg / Pfim / ImageSharp) selbst nachbaut, macht das der Host EINMAL
/// zentral. Bug-Fixes an EINER Stelle, alle Plugins profitieren.</para>
///
/// <para><b>Contracts v1.18.0+.</b> Bei aelteren Hosts default-implementiert
/// als <see cref="NullImageDecoder.Instance"/> (versucht nur den nativen
/// Avalonia-Bitmap-Ctor, kein Format-Fallback).</para>
///
/// <para><b>Thread-Safety:</b> Alle Methoden sind thread-safe und koennen
/// aus beliebigen Threads gerufen werden. Die tatsaechliche
/// <see cref="Bitmap"/>-Instanziierung passiert intern auf dem UI-Thread
/// (Skia hat Thread-Affinity fuer manche Formate). Der Aufrufer bekommt
/// die fertige Bitmap zurueck und kann sie direkt einer
/// <c>[ObservableProperty]</c>-Cover-Property zuweisen.</para></summary>
public interface IImageDecoder
{
    /// <summary>Dekodiert Bild-Bytes. Autodetection via Magic-Bytes
    /// (PNG/JPEG/BMP/GIF/WebP/AVIF/DDS). Rueckgabe <c>null</c> = kein
    /// Decode moeglich.</summary>
    Task<Bitmap?> DecodeAsync(byte[] bytes, CancellationToken ct = default);

    /// <summary>Dekodiert aus einem Stream (Position wird nicht zurueckgesetzt).</summary>
    Task<Bitmap?> DecodeAsync(Stream stream, CancellationToken ct = default);

    /// <summary>Convenience: Datei lesen und dekodieren. Rueckgabe
    /// <c>null</c> wenn Datei fehlt oder nicht dekodiert werden kann.</summary>
    Task<Bitmap?> DecodeFileAsync(string path, CancellationToken ct = default);

    /// <summary>Prueft nur die Magic-Bytes — keine Decode-Kosten. Nutzbar
    /// vor dem Cache-Write um sicherzustellen dass ein Download-Blob
    /// tatsaechlich ein Bild ist (nicht z.B. eine HTML-Login-Wall).</summary>
    bool LooksLikeImage(byte[] bytes);
}
