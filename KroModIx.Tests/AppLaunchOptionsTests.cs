using System;
using FluentAssertions;
using KroModIx.Services;
using Xunit;

namespace KroModIx.Tests;

public sealed class AppLaunchOptionsTests
{
    [Fact]
    public void Parse_no_args_leaves_everything_null()
    {
        var opts = AppLaunchOptions.Parse(Array.Empty<string>());
        opts.ApiPortOverride.Should().BeNull();
        opts.ApiTokenOverride.Should().BeNull();
        opts.AutoShutdownAfter.Should().BeNull();
        opts.RemainingArgs.Should().BeEmpty();
    }

    [Fact]
    public void Parse_recognizes_api_port_token_shutdown()
    {
        var opts = AppLaunchOptions.Parse(new[]
        {
            "--api-port", "5150",
            "--api-token", "abc123",
            "--auto-shutdown-after", "45s",
        });
        opts.ApiPortOverride.Should().Be(5150);
        opts.ApiTokenOverride.Should().Be("abc123");
        opts.AutoShutdownAfter.Should().Be(TimeSpan.FromSeconds(45));
        opts.RemainingArgs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("2m", 120)]
    [InlineData("1h", 3600)]
    public void Parse_duration_units(string raw, int expectedSeconds)
    {
        var opts = AppLaunchOptions.Parse(new[] { "--auto-shutdown-after", raw });
        opts.AutoShutdownAfter.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Parse_invalid_port_is_ignored()
    {
        var opts = AppLaunchOptions.Parse(new[] { "--api-port", "notanumber" });
        opts.ApiPortOverride.Should().BeNull();
    }

    [Fact]
    public void Parse_out_of_range_port_is_ignored()
    {
        var opts = AppLaunchOptions.Parse(new[] { "--api-port", "70000" });
        opts.ApiPortOverride.Should().BeNull();
    }

    [Fact]
    public void Parse_unknown_args_go_to_remaining()
    {
        var opts = AppLaunchOptions.Parse(new[] { "--framework", "Wayland", "--foo", "bar" });
        opts.RemainingArgs.Should().Equal("--framework", "Wayland", "--foo", "bar");
    }
}
