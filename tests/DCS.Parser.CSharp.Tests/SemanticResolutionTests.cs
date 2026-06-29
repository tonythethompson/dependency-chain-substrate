using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Parser.CSharp;
using DCS.Parser.CSharp.Semantic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp.Tests;

public sealed class SemanticResolutionTests
{
    [Fact]
    public void Resolves_types_in_namespace_with_usings()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            namespace MyApp.Services;
            public interface IFoo { }
            public class FooImpl : IFoo { }
            public static class Reg {
              public static void Configure(IServiceCollection services) {
                services.AddSingleton<IFoo, FooImpl>();
              }
            }
            """;

        var (nodes, _) = ParseWithSemantic(source, "Reg.cs");
        Assert.Single(nodes);
        Assert.Equal(TypeResolutionQuality.Resolved, nodes[0].TypeResolutionQuality);
        Assert.NotEmpty(nodes[0].AbstractToken.FullyQualifiedName);
        Assert.Contains("IFoo", nodes[0].AbstractToken.FullyQualifiedName);
    }

    [Fact]
    public void Fallback_does_not_populate_fqn()
    {
        var source = "services.AddSingleton<UnknownType>();";
        var (nodes, _) = ParseSyntacticOnly(source);
        Assert.Single(nodes);
        Assert.Equal(TypeResolutionQuality.SyntacticFallback, nodes[0].TypeResolutionQuality);
        Assert.True(string.IsNullOrEmpty(nodes[0].AbstractToken.FullyQualifiedName));
    }

    [Fact]
    public void Two_registrations_have_distinct_instance_ids()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            namespace Test;
            public interface IFoo { }
            public class FooImpl : IFoo { }
            public static class Reg {
              public static void Configure(IServiceCollection services) {
                services.AddSingleton<IFoo, FooImpl>();
                services.AddSingleton<IFoo, FooImpl>();
              }
            }
            """;

        var (nodes, _) = ParseWithSemantic(source, "Reg.cs");
        Assert.Equal(2, nodes.Count);
        Assert.NotEqual(nodes[0].Id, nodes[1].Id);
    }

    [Fact]
    public void TypeIdentityFormatter_value_type_nullable()
    {
        var tree = CSharpSyntaxTree.ParseText("int? x = null;");
        var compilation = CSharpCompilation.Create("test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var decl = tree.GetCompilationUnitRoot().DescendantNodes().OfType<VariableDeclarationSyntax>().First();
        var symbol = model.GetTypeInfo(decl.Type!).Type;
        var identity = TypeIdentityFormatter.Format(symbol, "scope");
        Assert.NotNull(identity);
        Assert.Contains("Nullable", identity!.MetadataName);
        Assert.Contains("Int32", identity.MetadataName);
    }

    [Fact]
    public void Cross_namespace_homonym_is_not_strict_duplicate()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            namespace AppA {
              public interface IFoo { }
              public class FooA : IFoo { }
            }
            namespace AppB {
              public interface IFoo { }
              public class FooB : IFoo { }
            }
            public static class Reg {
              public static void Configure(IServiceCollection services) {
                services.AddSingleton<AppA.IFoo, AppA.FooA>();
                services.AddSingleton<AppB.IFoo, AppB.FooB>();
              }
            }
            """;

        var (nodes, _) = ParseWithSemantic(source, "Reg.cs");
        Assert.Equal(2, nodes.Count);
        Assert.NotEqual(nodes[0].DuplicateGroupKey, nodes[1].DuplicateGroupKey);

        var graph = new RegistrationGraph { Nodes = nodes, SourceLanguage = "csharp" };
        var analysis = new GraphAnalyzer(graph).Analyze();
        Assert.Empty(analysis.Duplicates);
    }

    [Fact]
    public void Constructor_dependency_produces_resolved_edge()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-ctor-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "CtorTest.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Services.cs"), """
                using Microsoft.Extensions.DependencyInjection;
                namespace CtorTest;
                public interface IBar { }
                public class Bar : IBar { }
                public interface IFoo { }
                public class Foo : IFoo {
                  public Foo(IBar bar) { }
                }
                public static class Reg {
                  public static void Configure(IServiceCollection services) {
                    services.AddSingleton<IBar, Bar>();
                    services.AddSingleton<IFoo, Foo>();
                  }
                }
                """);

            var graph = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            }).ParseDirectory(root).SingleGraphOrDefault()!;

            Assert.Equal(2, graph.Nodes.Count);
            Assert.Single(graph.Edges);
            Assert.Empty(graph.UnresolvedInjections);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unverified_local_Add_extension_not_strict_duplicate_eligible()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            namespace Test;
            public class Widget { }
            public static class LocalDi {
              public static IServiceCollection AddSingleton<T>(this IServiceCollection s) where T : class => s;
            }
            public static class Reg {
              public static void Configure(IServiceCollection services) {
                services.AddSingleton<Widget>();
                services.AddSingleton<Widget>();
              }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source, path: "Reg.cs");
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location)
        };
        var compilation = CSharpCompilation.Create("Test", [tree], refs);
        var model = compilation.GetSemanticModel(tree);
        var scope = new ProjectTargetScope
        {
            ScopeId = "test|net8.0|Debug",
            CsprojPath = "Test.csproj",
            TargetFramework = "net8.0",
            AssemblyName = "Test",
            SourceMembershipProfileHash = "abc",
            SourceFiles = ["Reg.cs"]
        };
        var root = tree.GetCompilationUnitRoot();
        var usings = root.Usings.Select(u => u.Name?.ToString() ?? "").Where(u => u.Length > 0).ToList();
        var ctx = new SemanticRegistrationContext { Scope = scope, Model = model };
        var visitor = new RegistrationPatternVisitor("Reg.cs", usings, semantic: ctx);
        visitor.Visit(root);
        var nodes = visitor.Registrations.ToList();

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n =>
        {
            Assert.Equal(RegistrationRecognitionQuality.SyntaxCandidateUnverified, n.RegistrationRecognitionQuality);
            Assert.False(StrictDuplicateEligibility.IsEligible(n));
        });
    }

    private static (List<RegistrationNode> nodes, List<BlindSpotReport> spots) ParseSyntacticOnly(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Test.cs");
        var root = tree.GetCompilationUnitRoot();
        var usings = root.Usings.Select(u => u.Name?.ToString() ?? "").Where(u => u.Length > 0).ToList();
        var visitor = new RegistrationPatternVisitor("Test.cs", usings);
        visitor.Visit(root);
        return (visitor.Registrations.ToList(), visitor.BlindSpots.ToList());
    }

    private static (List<RegistrationNode> nodes, List<BlindSpotReport> spots) ParseWithSemantic(string source, string path)
    {
        var scope = new ProjectTargetScope
        {
            ScopeId = "test|net8.0|Debug",
            CsprojPath = "Test.csproj",
            TargetFramework = "net8.0",
            AssemblyName = "Test",
            SourceMembershipProfileHash = "abc",
            SourceFiles = [path],
            ImplicitUsingsUnmodeled = false
        };

        var scopeResult = ProjectTargetScopeCompilationFactory.Build(
            scope,
            new Dictionary<string, string> { [path] = source },
            []);

        var model = scopeResult.ModelsByPath[path];
        var root = model.SyntaxTree.GetCompilationUnitRoot();
        var usings = root.Usings.Select(u => u.Name?.ToString() ?? "").Where(u => u.Length > 0).ToList();
        var ctx = new SemanticRegistrationContext { Scope = scope, Model = model };
        var visitor = new RegistrationPatternVisitor(path, usings, semantic: ctx);
        visitor.Visit(root);
        return (visitor.Registrations.ToList(), visitor.BlindSpots.ToList());
    }
}
