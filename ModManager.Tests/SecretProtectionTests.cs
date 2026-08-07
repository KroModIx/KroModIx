using FluentAssertions;
using ModManager.Services;
using Xunit;

namespace ModManager.Tests;

public class SecretProtectionTests
{
    private readonly ISecretProtection _sut = new SecretProtection();

    [Fact]
    public void Protect_Then_Unprotect_Should_ReturnOriginal()
    {
        var secret = "sk-anthropic-test-1234567890";
        var cipher = _sut.Protect(secret);
        cipher.Should().NotBeNull().And.NotBe(secret);
        cipher.Should().StartWith("v1:");
        _sut.Unprotect(cipher).Should().Be(secret);
    }

    [Fact]
    public void Protect_Null_Should_ReturnNull()
    {
        _sut.Protect(null).Should().BeNull();
        _sut.Protect("").Should().BeNull();
    }

    [Fact]
    public void Unprotect_InvalidFormat_Should_ReturnNull()
    {
        _sut.Unprotect("not-a-secret").Should().BeNull();
        _sut.Unprotect("v1:not-base64!").Should().BeNull();
    }
}
