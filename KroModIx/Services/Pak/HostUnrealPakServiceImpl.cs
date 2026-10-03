using System;
using System.IO;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Pak;

/// <summary>Host-Implementierung von <see cref="IUnrealPakService"/> — eine
/// dünne Fassade über <see cref="UnrealPakReader"/> und
/// <see cref="UnrealPakWriter"/>.
///
/// <para>Die Fassade existiert, damit die Contracts frei von
/// Implementierungstypen bleiben: Leser und Schreiber sind
/// <c>internal</c> und erfüllen die Contracts-Schnittstellen, nach außen
/// gibt es nur <see cref="IUnrealPakReader"/> und
/// <see cref="IUnrealPakBuilder"/>.</para></summary>
public sealed class HostUnrealPakServiceImpl : IUnrealPakService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Die Pak-Magic, wie sie im Footer auf der Platte steht
    /// (little-endian).</summary>
    private static readonly byte[] FooterMagic = [0xE1, 0x12, 0x6F, 0x5A];

    /// <summary>Wie viele Bytes am Dateiende nach der Magic durchsucht
    /// werden. Die genaue Position hängt von der Pak-Version ab, deshalb ein
    /// Fenster statt eines festen Offsets — 1 KiB deckt jede bekannte
    /// Footer-Variante ab.</summary>
    private const int FooterWindow = 1024;

    public bool IsPakFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = File.OpenRead(path);
            if (fs.Length < FooterMagic.Length) return false;
            var tailLength = (int)Math.Min(FooterWindow, fs.Length);
            var tail = new byte[tailLength];
            fs.Seek(-tailLength, SeekOrigin.End);
            fs.ReadExactly(tail, 0, tailLength);
            return tail.AsSpan().IndexOf(FooterMagic) >= 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Pak-Erkennung fehlgeschlagen: {Path}", path);
            return false;
        }
    }

    public IUnrealPakReader OpenRead(string path) => UnrealPakReader.Open(path);

    public IUnrealPakBuilder CreateBuilder(string? mountPoint = null)
        => new UnrealPakWriter(mountPoint);
}
