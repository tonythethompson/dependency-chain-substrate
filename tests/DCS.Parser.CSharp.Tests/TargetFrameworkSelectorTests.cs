using DCS.Parser.CSharp.Semantic;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class TargetFrameworkSelectorTests
{
    [Theory]
    [InlineData("csharp|net10.0", "net10.0")]
    [InlineData("csharp|net10.0-windows10.0.19041.0", "net10.0-windows10.0.19041.0")]
    [InlineData("net10.0", "net10.0")]
    [InlineData("csharp", null)]
    [InlineData("java|com.example.App", null)]
    public void TryParseContextTargetFramework(string contextId, string? expected) =>
        Assert.Equal(expected, TargetFrameworkSelector.TryParseContextTargetFramework(contextId));

    [Fact]
    public void SelectPrimaryTargetFramework_prefers_portable_net10()
    {
        var tfms = new[] { "net10.0-windows10.0.19041.0", "net10.0", "net8.0" };
        Assert.Equal("net10.0", TargetFrameworkSelector.SelectPrimaryTargetFramework(tfms));
    }
}
