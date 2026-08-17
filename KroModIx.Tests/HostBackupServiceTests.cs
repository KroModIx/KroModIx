// xUnit1051: TestContext.Current.CancellationToken ist in dieser
// Codebase nicht verwendet; Tests laufen synchron und sind schnell.
#pragma warning disable xUnit1051
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Services;
using KroModIx.Services.Backup;
using Xunit;

namespace KroModIx.Tests;

/// <summary>Guard fuer den v1.23.0-Backup-Baukasten. Nutzt XDG_STATE_HOME =
/// TempDir damit die Tests nicht auf ~/.local/share/KroModIx/backups
/// schreiben (Interferenz mit realer User-Config).</summary>
public class HostBackupServiceTests : IDisposable
{
    private readonly string _tempStateRoot;
    private readonly string? _origXdg;

    public HostBackupServiceTests()
    {
        _tempStateRoot = Path.Combine(Path.GetTempPath(),
            "kromodix-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempStateRoot);
        _origXdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", _tempStateRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", _origXdg);
        try { Directory.Delete(_tempStateRoot, recursive: true); } catch { }
    }

    private static string CreateModDir(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "kmix-mod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "mod.txt"), "hello");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.bin"), "world");
        return root;
    }

    [Fact]
    public async Task CreateSnapshot_ProducesZipAndMeta()
    {
        var svc = new HostBackupServiceImpl();
        var dir = CreateModDir(out var root);
        try
        {
            var snap = await svc.CreateSnapshotAsync(
                pluginId: "test.plugin", gameKey: "steam:123",
                directories: new[] { root }, label: "Vor Install");
            snap.Id.Should().NotBeNullOrEmpty();
            File.Exists(snap.ZipPath).Should().BeTrue();
            snap.ZipBytes.Should().BeGreaterThan(0);
            snap.SourceDirectories.Should().ContainSingle();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ListSnapshots_FiltersByGameKey()
    {
        var svc = new HostBackupServiceImpl();
        var dir = CreateModDir(out var root);
        try
        {
            await svc.CreateSnapshotAsync("test.plugin", "steam:1", new[] { root }, "A");
            await svc.CreateSnapshotAsync("test.plugin", "steam:2", new[] { root }, "B");
            await svc.CreateSnapshotAsync("test.plugin", "steam:1", new[] { root }, "C");

            var g1 = await svc.ListSnapshotsAsync("test.plugin", "steam:1");
            g1.Should().HaveCount(2);
            g1[0].Label.Should().Be("C"); // neueste zuerst
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Prune_KeepsNewest()
    {
        var svc = new HostBackupServiceImpl();
        var dir = CreateModDir(out var root);
        try
        {
            for (int i = 0; i < 4; i++)
            {
                await svc.CreateSnapshotAsync("test.plugin", "steam:9", new[] { root }, $"snap-{i}");
                await Task.Delay(20); // damit die IDs unterscheidbar sind
            }
            var pruned = await svc.PruneAsync("test.plugin", "steam:9", keepLast: 2);
            pruned.Should().Be(2);
            (await svc.ListSnapshotsAsync("test.plugin", "steam:9")).Should().HaveCount(2);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Restore_ReplacesTargetAndKeepsPreRestoreBackup()
    {
        var svc = new HostBackupServiceImpl();
        var dir = CreateModDir(out var root);
        try
        {
            var snap = await svc.CreateSnapshotAsync("test.plugin", "steam:5",
                new[] { root }, "orig");

            // User haette danach den Mod-Ordner „kaputt gemacht"
            File.WriteAllText(Path.Combine(root, "mod.txt"), "MODIFIED");

            var ok = await svc.RestoreSnapshotAsync(snap.Id);
            ok.Should().BeTrue();
            File.ReadAllText(Path.Combine(root, "mod.txt")).Should().Be("hello");

            // Der modifizierte Zustand liegt jetzt als .pre-restore-<ts>/ daneben
            var parent = Path.GetDirectoryName(root)!;
            var safety = Directory.GetDirectories(parent, Path.GetFileName(root) + ".pre-restore-*");
            safety.Should().NotBeEmpty();
            File.ReadAllText(Path.Combine(safety[0], "mod.txt")).Should().Be("MODIFIED");
            foreach (var s in safety) Directory.Delete(s, recursive: true);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesZipAndMeta()
    {
        var svc = new HostBackupServiceImpl();
        var dir = CreateModDir(out var root);
        try
        {
            var snap = await svc.CreateSnapshotAsync("test.plugin", "steam:7",
                new[] { root }, "toDelete");
            var ok = await svc.DeleteSnapshotAsync(snap.Id);
            ok.Should().BeTrue();
            File.Exists(snap.ZipPath).Should().BeFalse();
            (await svc.ListSnapshotsAsync("test.plugin", "steam:7")).Should().BeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
