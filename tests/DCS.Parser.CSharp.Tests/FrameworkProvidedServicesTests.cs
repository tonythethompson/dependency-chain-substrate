using DCS.Parser.CSharp.Semantic;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class FrameworkProvidedServicesTests
{
    [Theory]
    [InlineData("ILogger<MyApp>", true)]
    [InlineData("IOptions<MySettings>", true)]
    [InlineData("IRepository<Foo>", false)]
    [InlineData("IValidator<Bar>", false)]
    public void IsFrameworkProvided_suppresses_only_known_framework_generics(string typeName, bool expected)
    {
        Assert.Equal(expected, FrameworkProvidedServices.IsFrameworkProvided(typeName));
    }
}
