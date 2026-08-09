using FluentAssertions;
using KroModIx.Services;
using Xunit;

namespace KroModIx.Tests;

public class AppPathsTests
{
    [Fact]
    public void ConfigRoot_Should_ExistAfterFirstAccess()
    {
        var path = AppPaths.ConfigRoot;
        path.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void CacheRoot_Should_BeSeparateFromConfigRoot()
    {
        AppPaths.CacheRoot.Should().NotBe(AppPaths.ConfigRoot);
    }

    [Fact]
    public void UserPluginsDir_Should_LiveUnderConfigRoot()
    {
        AppPaths.UserPluginsDir.Should().StartWith(AppPaths.ConfigRoot);
    }

    [Fact]
    public void GameCoverCacheDir_Should_LiveUnderCacheRoot()
    {
        AppPaths.GameCoverCacheDir.Should().StartWith(AppPaths.CacheRoot);
    }
}
