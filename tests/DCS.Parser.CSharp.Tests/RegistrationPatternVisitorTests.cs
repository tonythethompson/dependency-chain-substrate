using DCS.Core.IR;
using DCS.Parser.CSharp;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class RegistrationPatternVisitorTests
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
    public void Detects_closed_generic_two_type_args()
    {
        var (nodes, _) = Parse("""
            services.AddSingleton<IFoo, FooImpl>();
            """);
        Assert.Single(nodes);
        Assert.Equal("IFoo", nodes[0].AbstractToken.ShortName);
        Assert.Equal("FooImpl", nodes[0].ConcreteImpl?.ShortName);
        Assert.Equal(Lifetime.Singleton, nodes[0].Lifetime);
        Assert.Equal(Confidence.Explicit, nodes[0].ParserConfidence);
    }

    [Fact]
    public void Detects_scoped_and_transient()
    {
        var (nodes, _) = Parse("""
            services.AddScoped<IBar, BarImpl>();
            services.AddTransient<IBaz, BazImpl>();
            """);
        Assert.Equal(2, nodes.Count);
        Assert.Equal(Lifetime.Scoped, nodes[0].Lifetime);
        Assert.Equal(Lifetime.Transient, nodes[1].Lifetime);
    }

    [Fact]
    public void Detects_self_registration_as_single_type_arg()
    {
        var (nodes, _) = Parse("services.AddSingleton<FooImpl>();");
        Assert.Single(nodes);
        Assert.Equal("FooImpl", nodes[0].AbstractToken.ShortName);
        Assert.Null(nodes[0].ConcreteImpl); // self-registration: ConcreteImpl omitted
    }

    [Fact]
    public void Detects_typeof_pattern()
    {
        var (nodes, _) = Parse("""
            services.AddSingleton(typeof(IFoo), typeof(FooImpl));
            """);
        Assert.Single(nodes);
        Assert.Equal("IFoo", nodes[0].AbstractToken.ShortName);
        Assert.Equal("FooImpl", nodes[0].ConcreteImpl?.ShortName);
    }

    [Fact]
    public void Factory_lambda_produces_blind_spot()
    {
        var (nodes, spots) = Parse("""
            services.AddSingleton<IFoo>(sp => new FooImpl());
            """);
        Assert.Single(nodes);
        Assert.Equal(Confidence.BlindSpot, nodes[0].ParserConfidence);
        Assert.Contains(spots, s => s.Pattern == "factory_lambda");
    }

    [Fact]
    public void Extension_method_produces_blind_spot_report()
    {
        var (nodes, spots) = Parse("services.AddLogging();");
        Assert.Empty(nodes);
        Assert.Contains(spots, s => s.Pattern == "extension_method");
    }

    [Fact]
    public void Try_add_annotated()
    {
        var (nodes, _) = Parse("services.TryAddSingleton<IFoo, FooImpl>();");
        Assert.Single(nodes);
        Assert.True(nodes[0].Annotations.ContainsKey("conditional"));
        Assert.Equal("try_add", nodes[0].Annotations["conditional"]);
    }

    [Fact]
    public void Framework_tags_inferred_from_avalonia_using()
    {
        var (nodes, _) = Parse("""
            using Avalonia.Controls;
            services.AddSingleton<IFoo, FooImpl>();
            """);
        Assert.Single(nodes);
        Assert.Contains("avalonia", nodes[0].FrameworkTags);
    }

    [Fact]
    public void Framework_tags_inferred_from_winui_using()
    {
        var (nodes, _) = Parse("""
            using Microsoft.UI.Xaml;
            services.AddSingleton<IFoo, FooImpl>();
            """);
        Assert.Single(nodes);
        Assert.Contains("winui", nodes[0].FrameworkTags);
    }
}
