using FluentAssertions;
using KroModIx.Services.Ai;
using Xunit;

namespace KroModIx.Tests;

public sealed class SystemHardwareServiceTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)]
    [InlineData("AMD Radeon RX 7900 XTX", GpuVendor.Amd)]
    [InlineData("Intel Iris Xe Graphics", GpuVendor.Intel)]
    [InlineData("Apple M2 Pro", GpuVendor.AppleSilicon)]
    [InlineData("Something else", GpuVendor.Unknown)]
    public void VendorFromName_recognizes_common_gpus(string name, GpuVendor expected)
    {
        SystemHardwareService.VendorFromName(name).Should().Be(expected);
    }

    [Fact]
    public void ParseWindowsWmicCsv_takes_largest_adapter_ram()
    {
        // Intel iGPU (kleiner) + Nvidia (größer) — Nvidia gewinnt
        var csv = "Node,AdapterRAM,Name\r\n" +
                  "PC-01,4293918720,NVIDIA GeForce RTX 3060\r\n" +
                  "PC-01,1048576000,Intel(R) UHD Graphics\r\n";
        var info = SystemHardwareService.ParseWindowsWmicCsv(csv);
        info.Should().NotBeNull();
        info!.Vendor.Should().Be(GpuVendor.Nvidia);
        info.VramGb.Should().BeApproximately(4.0, 0.5);
    }

    [Fact]
    public void ParseWindowsWmicCsv_returns_null_when_empty()
    {
        SystemHardwareService.ParseWindowsWmicCsv("").Should().BeNull();
        SystemHardwareService.ParseWindowsWmicCsv("Node,AdapterRAM,Name\r\n").Should().BeNull();
    }

    [Fact]
    public void ParseWindowsPowerShellJson_array_shape()
    {
        var json = """[{"AdapterRAM":16000000000,"Name":"AMD Radeon RX 7900 XTX"},{"AdapterRAM":536870912,"Name":"Microsoft Basic Display"}]""";
        var info = SystemHardwareService.ParseWindowsPowerShellJson(json);
        info.Should().NotBeNull();
        info!.Vendor.Should().Be(GpuVendor.Amd);
        info.VramGb.Should().BeApproximately(14.9, 0.3);
    }

    [Fact]
    public void ParseWindowsPowerShellJson_single_object_shape()
    {
        var json = """{"AdapterRAM":8589934592,"Name":"NVIDIA GeForce RTX 3070"}""";
        var info = SystemHardwareService.ParseWindowsPowerShellJson(json);
        info.Should().NotBeNull();
        info!.VramGb.Should().BeApproximately(8.0, 0.1);
    }

    [Fact]
    public void ExtractLspciName_parses_four_quoted_fields()
    {
        var line = "01:00.0 \"VGA compatible controller [0300]\" \"NVIDIA Corporation [10de]\" \"GeForce RTX 4080 SUPER [2704]\" -rXX";
        var name = SystemHardwareService.ExtractLspciName(line);
        name.Should().Be("NVIDIA Corporation GeForce RTX 4080 SUPER");
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080 SUPER", 16)]
    [InlineData("NVIDIA GeForce RTX 4090", 24)]
    [InlineData("GeForce RTX 3080", 10)]
    [InlineData("RTX 3060", 12)]
    [InlineData("GTX 1080 Ti", 11)]
    [InlineData("NVIDIA RTX A6000", 48)]
    [InlineData("Some Unknown Card", 0)]
    public void LookupNvidiaVram_covers_common_models(string name, double expectedGb)
    {
        SystemHardwareService.LookupNvidiaVram(name).Should().Be(expectedGb);
    }
}

public sealed class OllamaCuratedModelsTests
{
    [Fact]
    public void CpuOnly_returns_only_small_models()
    {
        var models = OllamaCuratedModels.RecommendedFor(0);
        models.Should().NotBeEmpty();
        models.Should().OnlyContain(m => m.MinVramGb <= 0);
        // gemma3:1b + qwen2.5:3b + llama3.2:3b + phi3.5:3.8b
        models.Should().Contain(m => m.Name.StartsWith("gemma3:1b"));
    }

    [Fact]
    public void _8gb_matches_7b_range()
    {
        var models = OllamaCuratedModels.RecommendedFor(8);
        models.Should().Contain(m => m.Name == "llama3.1:8b");
        models.Should().Contain(m => m.Name == "qwen2.5:7b");
        // Nicht 32b oder 70b
        models.Should().NotContain(m => m.Name.Contains("70b") || m.Name.Contains("72b"));
    }

    [Fact]
    public void _16gb_matches_14b_and_gemma3_12b()
    {
        var models = OllamaCuratedModels.RecommendedFor(16);
        models.Should().Contain(m => m.Name == "qwen2.5:14b");
        models.Should().Contain(m => m.Name == "gemma3:12b");
    }

    [Fact]
    public void _24gb_matches_32b_range()
    {
        var models = OllamaCuratedModels.RecommendedFor(24);
        models.Should().Contain(m => m.Name == "qwen2.5:32b" || m.Name == "gemma3:27b");
    }

    [Fact]
    public void _48gb_matches_70b_range()
    {
        var models = OllamaCuratedModels.RecommendedFor(48);
        models.Should().Contain(m => m.Name.Contains("70b") || m.Name.Contains("72b"));
    }
}
