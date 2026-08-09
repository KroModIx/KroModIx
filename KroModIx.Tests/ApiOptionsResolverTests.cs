using FluentAssertions;
using KroModIx.Services;
using KroModIx.Services.Api;
using Xunit;

namespace KroModIx.Tests;

public sealed class ApiOptionsResolverTests
{
    [Fact]
    public void Settings_disabled_and_no_cli_yields_disabled()
    {
        var s = new AppSettings { ApiEnabled = false, ApiPort = 5100, ApiBearerToken = "tok" };
        var cli = new AppLaunchOptions();
        var opts = ApiOptionsResolver.Resolve(s, cli);
        opts.Enabled.Should().BeFalse();
        opts.Port.Should().Be(5100);
        opts.BearerToken.Should().Be("tok");
    }

    [Fact]
    public void Settings_enabled_gives_enabled()
    {
        var s = new AppSettings { ApiEnabled = true, ApiPort = 5100, ApiBearerToken = "tok" };
        var opts = ApiOptionsResolver.Resolve(s, new AppLaunchOptions());
        opts.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Cli_port_overrides_and_forces_enabled()
    {
        var s = new AppSettings { ApiEnabled = false, ApiPort = 5100, ApiBearerToken = "settings-tok" };
        var cli = new AppLaunchOptions { ApiPortOverride = 5199, ApiTokenOverride = "cli-tok" };
        var opts = ApiOptionsResolver.Resolve(s, cli);
        opts.Enabled.Should().BeTrue();
        opts.Port.Should().Be(5199);
        opts.BearerToken.Should().Be("cli-tok");
    }

    [Fact]
    public void Cli_token_only_keeps_settings_port()
    {
        var s = new AppSettings { ApiEnabled = true, ApiPort = 5100, ApiBearerToken = "settings-tok" };
        var cli = new AppLaunchOptions { ApiTokenOverride = "cli-tok" };
        var opts = ApiOptionsResolver.Resolve(s, cli);
        opts.Port.Should().Be(5100);
        opts.BearerToken.Should().Be("cli-tok");
    }
}
