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
    public void Factory_lambda_produces_shallow_factory_blind_spot()
    {
        var (nodes, spots) = Parse("""
            services.AddSingleton<IFoo>(sp => new FooImpl());
            """);
        Assert.Single(nodes);
        Assert.Equal(Confidence.Degraded, nodes[0].ParserConfidence);
        Assert.Contains(spots, s => s.Pattern == "factory_lambda_shallow");
        Assert.Equal("factory_lambda_shallow", nodes[0].Annotations.GetValueOrDefault("pattern"));
    }

    [Fact]
    public void Non_generic_shallow_factory_lambda_registers_created_type()
    {
        var (nodes, spots) = Parse("""
            services.AddSingleton(sp => new MainWindow());
            """);
        Assert.Single(nodes);
        Assert.Equal("MainWindow", nodes[0].DisplayName);
        Assert.Contains(spots, s => s.Pattern == "factory_lambda_shallow");
    }

    [Fact]
    public void Block_factory_lambda_registers_returned_created_type()
    {
        var (nodes, spots) = Parse("""
            services.AddSingleton(sp =>
            {
                var vm = sp.GetRequiredService<MainWindowViewModel>();
                return new MainWindow(vm);
            });
            """);
        Assert.Single(nodes);
        Assert.Equal("MainWindow", nodes[0].DisplayName);
        Assert.DoesNotContain(spots, s => s.Pattern == "unrecognized_pattern");
        Assert.Contains(spots, s => s.Pattern == "factory_lambda_shallow");
        Assert.True(nodes[0].Annotations.TryGetValue("factory_lambda_service_keys", out var keys));
        Assert.Contains("MainWindowViewModel", keys, StringComparison.Ordinal);
    }

    [Fact]
    public void Shallow_factory_lambda_records_get_required_service_keys()
    {
        var (nodes, _) = Parse("services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<IHandler>()));");
        Assert.Single(nodes);
        Assert.True(nodes[0].Annotations.TryGetValue("factory_lambda_service_keys", out var keys));
        Assert.Contains("IHandler", keys, StringComparison.Ordinal);
    }

    [Fact]
    public void Block_factory_lambda_with_local_variable_registers_created_type()
    {
        var (nodes, spots) = Parse("""
            services.AddTransient(sp =>
            {
                var window = new DevLogWindow();
                return window;
            });
            """);
        Assert.Single(nodes);
        Assert.Equal("DevLogWindow", nodes[0].DisplayName);
        Assert.DoesNotContain(spots, s => s.Pattern == "unrecognized_pattern");
        Assert.Contains(spots, s => s.Pattern == "factory_lambda_shallow");
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
    public void If_else_branches_annotated_as_conditional()
    {
        var (nodes, _) = Parse("""
            if (useDynamoDb)
                services.AddScoped<IJobQueue, DynamoDbJobQueue>();
            else
                services.AddSingleton<IJobQueue, InMemoryJobQueue>();
            """);
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.Equal("if_else", n.Annotations.GetValueOrDefault("conditional")));
        Assert.Contains(nodes, n => n.Annotations.GetValueOrDefault("conditional_branch") == "if");
        Assert.Contains(nodes, n => n.Annotations.GetValueOrDefault("conditional_branch") == "else");
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
