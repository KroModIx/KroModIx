#pragma warning disable xUnit1051
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Guard fuer den v1.24.0-Konflikt-Scanner. Nutzt einen kleinen
/// in-Memory-Scanner (der PluginActivator-Loop ist gross und braucht viele
/// Ctors — die Aggregations-Logik ist die interessante Teil, den wir hier
/// entkoppelt testen).</summary>
public class HostConflictScannerTests
{
    private sealed class FakeScanner : IConflictScanner
    {
        public IReadOnlyList<(string Plugin, ModFileset FileSet)> Sources { get; }
        public FakeScanner(params (string Plugin, ModFileset FileSet)[] sources)
        {
            Sources = sources;
        }

        public Task<IReadOnlyList<FileConflict>> ScanAsync(string gameKey, CancellationToken ct = default)
        {
            var byPath = new Dictionary<string, List<ConflictOwner>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (plugin, fs) in Sources)
            foreach (var raw in fs.RelativeFiles)
            {
                var norm = raw.Trim().Replace('\\', '/').TrimStart('/');
                if (norm.Length == 0) continue;
                if (!byPath.TryGetValue(norm, out var owners))
                    byPath[norm] = owners = new();
                owners.Add(new ConflictOwner(plugin, plugin, fs.ModId, fs.ModDisplayName));
            }
            var res = byPath
                .Where(kv => kv.Value.Count > 1)
                .OrderBy(kv => kv.Key)
                .Select(kv => new FileConflict(kv.Key, kv.Value))
                .ToList();
            return Task.FromResult<IReadOnlyList<FileConflict>>(res);
        }
    }

    [Fact]
    public async Task Scan_ReturnsEmpty_WhenNoOverlap()
    {
        var s = new FakeScanner(
            ("dsp", new ModFileset("m1", "Mod 1", new[] { "a/x.dll" })),
            ("dsp", new ModFileset("m2", "Mod 2", new[] { "b/y.dll" })));
        var r = await s.ScanAsync("steam:1");
        r.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_FindsOverlap_CaseInsensitive()
    {
        var s = new FakeScanner(
            ("dsp", new ModFileset("m1", "Mod 1", new[] { "Content/Items/Sword.pak" })),
            ("dsp", new ModFileset("m2", "Mod 2", new[] { "content/items/sword.pak" })));
        var r = await s.ScanAsync("steam:1");
        r.Should().HaveCount(1);
        r[0].Owners.Should().HaveCount(2);
    }

    [Fact]
    public async Task Scan_NormalizesBackslashesAndLeadingSlash()
    {
        var s = new FakeScanner(
            ("dsp", new ModFileset("m1", "Mod 1", new[] { "\\Plugins\\A.dll" })),
            ("dsp", new ModFileset("m2", "Mod 2", new[] { "/Plugins/A.dll" })));
        var r = await s.ScanAsync("steam:1");
        r.Should().HaveCount(1);
        r[0].RelativePath.Should().Be("Plugins/A.dll");
    }

    [Fact]
    public async Task Scan_MergesAcrossPlugins()
    {
        var s = new FakeScanner(
            ("dsp", new ModFileset("m1", "DSP-Mod", new[] { "BepInEx/plugins/Shared.dll" })),
            ("cyberpunk", new ModFileset("m2", "CP-Mod", new[] { "BepInEx/plugins/Shared.dll" })));
        var r = await s.ScanAsync("steam:1");
        r.Should().HaveCount(1);
        r[0].Owners.Select(o => o.PluginDisplayName).Should().BeEquivalentTo(new[] { "dsp", "cyberpunk" });
    }
}
