using DCS.Core.IR;
using DCS.Parser.CSharp;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class OptionsAndHttpClientRegistrationTests
{
    private static (List<RegistrationNode> nodes, List<BlindSpotReport> spots) Parse(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Test.cs");
        var root = tree.GetCompilationUnitRoot();
        var usings = root.Usings
            .Select(u => u.Name?.ToString() ?? string.Empty)
            .Where(u => u.Length > 0)
            .ToList();
        var visitor = new RegistrationPatternVisitor("Test.cs", usings);
        visitor.Visit(root);
        return (visitor.Registrations.ToList(), visitor.BlindSpots.ToList());
    }

    [Fact]
    public void Configure_emits_options_configuration_node()
    {
        var (nodes, _) = Parse("""
            services.Configure<MyOptions>(configuration.GetSection("My"));
            """);
        Assert.Single(nodes);
        Assert.Equal("MyOptions", nodes[0].DisplayName);
        Assert.Equal("options_configuration", nodes[0].Annotations.GetValueOrDefault("pattern"));
    }

    [Fact]
    public void Typed_AddHttpClient_emits_transient_registration()
    {
        var (nodes, spots) = Parse("""
            services.AddHttpClient<IMyClient, MyClient>();
            """);
        Assert.Single(nodes);
        Assert.Equal("IMyClient", nodes[0].AbstractToken.ShortName);
        Assert.Equal("MyClient", nodes[0].ConcreteImpl?.ShortName);
        Assert.Equal(Lifetime.Transient, nodes[0].Lifetime);
        Assert.DoesNotContain(spots, s => s.Pattern == "extension_method");
    }

    [Fact]
    public void Untyped_AddHttpClient_emits_extension_blind_spot()
    {
        var (_, spots) = Parse("""
            services.AddHttpClient("named", client => client.Timeout = TimeSpan.FromSeconds(30));
            """);
        Assert.Contains(spots, s => s.Pattern == "extension_method");
    }
}
