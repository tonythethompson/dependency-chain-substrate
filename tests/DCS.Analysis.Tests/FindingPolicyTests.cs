using DCS.Analysis;
using Xunit;

namespace DCS.Analysis.Tests;

public sealed class FindingPolicyTests
{
    [Theory]
    [InlineData("ILogger<MyApp>", false)]
    [InlineData("IOptions<MySettings>", false)]
    [InlineData("IStringLocalizer<SharedResource>", false)]
    [InlineData("IRepository<Foo>", true)]
    [InlineData("IValidator<Bar>", true)]
    public void IsActionableUnresolved_suppresses_only_framework_generics(string typeName, bool expectedActionable)
    {
        var actionable = FindingPolicy.IsActionableUnresolved(typeName);
        Assert.Equal(expectedActionable, actionable);
    }
}
