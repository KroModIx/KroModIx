using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Services.Api;
using Xunit;

namespace KroModIx.Tests;

public sealed class PluginRouteMatcherTests
{
    private static PluginApiEndpoint Ep(string method, string path) =>
        new(method, path, null,
            (_, _) => Task.FromResult(PluginApiResponse.NoContent()));

    [Fact]
    public void Exact_static_match()
    {
        var eps = new[] { Ep("GET", "mods") };
        var m = PluginRouteMatcher.TryMatch(eps, "GET", "mods");
        m.Should().NotBeNull();
        m!.Value.Endpoint.RelativePath.Should().Be("mods");
        m.Value.RouteValues.Should().BeEmpty();
    }

    [Fact]
    public void Method_mismatch_returns_null()
    {
        var eps = new[] { Ep("GET", "mods") };
        PluginRouteMatcher.TryMatch(eps, "POST", "mods").Should().BeNull();
    }

    [Fact]
    public void Route_parameter_is_extracted()
    {
        var eps = new[] { Ep("GET", "mods/{id}") };
        var m = PluginRouteMatcher.TryMatch(eps, "GET", "mods/42");
        m.Should().NotBeNull();
        m!.Value.RouteValues["id"].Should().Be("42");
    }

    [Fact]
    public void Multiple_route_parameters()
    {
        var eps = new[] { Ep("PUT", "categories/{cat}/mods/{modId}") };
        var m = PluginRouteMatcher.TryMatch(eps, "PUT", "categories/tools/mods/abc");
        m.Should().NotBeNull();
        m!.Value.RouteValues["cat"].Should().Be("tools");
        m.Value.RouteValues["modId"].Should().Be("abc");
    }

    [Fact]
    public void Segment_count_mismatch_returns_null()
    {
        var eps = new[] { Ep("GET", "mods/{id}") };
        PluginRouteMatcher.TryMatch(eps, "GET", "mods").Should().BeNull();
        PluginRouteMatcher.TryMatch(eps, "GET", "mods/42/extra").Should().BeNull();
    }

    [Fact]
    public void First_matching_endpoint_wins()
    {
        // Bewusstes Verhalten: Plugin ist selbst für die Reihenfolge zuständig.
        // Statische vor dynamischen Routen zu registrieren ist die Aufgabe des Plugin-Autors.
        var eps = new[] { Ep("GET", "mods/latest"), Ep("GET", "mods/{id}") };
        var m = PluginRouteMatcher.TryMatch(eps, "GET", "mods/latest");
        m!.Value.Endpoint.RelativePath.Should().Be("mods/latest");
    }

    [Fact]
    public void Method_matching_is_case_insensitive()
    {
        var eps = new[] { Ep("get", "hello") };
        PluginRouteMatcher.TryMatch(eps, "GET", "hello").Should().NotBeNull();
        PluginRouteMatcher.TryMatch(eps, "Get", "hello").Should().NotBeNull();
    }

    [Fact]
    public void Leading_and_trailing_slashes_are_tolerated()
    {
        var eps = new[] { Ep("GET", "/mods/") };
        PluginRouteMatcher.TryMatch(eps, "GET", "mods").Should().NotBeNull();
        PluginRouteMatcher.TryMatch(eps, "GET", "/mods/").Should().NotBeNull();
    }
}
