using System.Text;
using System.Text.Json;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using Xunit;

namespace KroModIx.Tests;

public sealed class PluginApiResponseTests
{
    [Fact]
    public void Json_serializes_camelCase_and_ignores_null()
    {
        var r = PluginApiResponse.Json(new { CamelName = "x", Ignored = (string?)null });
        r.StatusCode.Should().Be(200);
        r.ContentType.Should().StartWith("application/json");
        var body = Encoding.UTF8.GetString(r.Body!);
        body.Should().Contain("\"camelName\":\"x\"");
        body.Should().NotContain("ignored");
    }

    [Fact]
    public void Json_custom_status()
    {
        var r = PluginApiResponse.Json(new { ok = true }, statusCode: 201);
        r.StatusCode.Should().Be(201);
    }

    [Fact]
    public void NoContent_has_null_body()
    {
        var r = PluginApiResponse.NoContent();
        r.StatusCode.Should().Be(204);
        r.Body.Should().BeNull();
        r.ContentType.Should().BeNull();
    }

    [Fact]
    public void NotFound_returns_problem_json()
    {
        var r = PluginApiResponse.NotFound("no such thing");
        r.StatusCode.Should().Be(404);
        r.ContentType.Should().Be("application/problem+json");
        var doc = JsonDocument.Parse(r.Body!);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Not Found");
        doc.RootElement.GetProperty("detail").GetString().Should().Be("no such thing");
    }

    [Fact]
    public void Text_default_is_plain_utf8()
    {
        var r = PluginApiResponse.Text("hallo");
        r.ContentType.Should().Contain("text/plain");
        Encoding.UTF8.GetString(r.Body!).Should().Be("hallo");
    }
}
