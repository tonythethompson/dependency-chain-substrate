using DCS.Analysis;
using DCS.Core.IR;
using DCS.Fix;
using DCS.Parser.CSharp;

namespace DCS.Fix.Tests;

public sealed class BrokenFixPlannerTests
{
    [Fact]
    public void Plan_skips_factory_with_service_locator()
    {
        var root = CreateBrokenFixture(includeComplexDep: true);
        try
        {
            var graph = ParseGraph(root);
            var analysis = new GraphAnalyzer(graph).Analyze();
            var proposals = BrokenFixPlanner.Plan(root, graph, analysis);
            Assert.Empty(proposals);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Plan_proposes_simple_factory_conversion()
    {
        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            var graph = ParseGraph(root);
            var analysis = new GraphAnalyzer(graph).Analyze();
            Assert.NotEmpty(analysis.BrokenChains);

            var proposals = BrokenFixPlanner.Plan(root, graph, analysis);
            Assert.Single(proposals);
            Assert.Equal("IDepService", proposals[0].TargetDisplayName);
            Assert.Contains("IDepService", proposals[0].ReplacementStatement);
            Assert.Contains("DepServiceImpl", proposals[0].ReplacementStatement);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_converts_factory_and_clears_broken_chain()
    {
        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            var graph = ParseGraph(root);
            var before = new GraphAnalyzer(graph).Analyze();
            Assert.NotEmpty(before.BrokenChains);

            var result = FixEngine.ApplyBrokenFixes(root, graph, before, forceDirtyTree: true);
            Assert.Single(result.Proposals);

            var afterGraph = ParseGraph(root);
            var after = new GraphAnalyzer(afterGraph).Analyze();
            Assert.Empty(after.BrokenChains);

            FixSafetyGuard.VerifyApplyGuards(before, after, root, result.Patches);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_throws_when_working_tree_dirty()
    {
        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            var graph = ParseGraph(root);
            var analysis = new GraphAnalyzer(graph).Analyze();
            File.WriteAllText(Path.Combine(root, "dirty.txt"), "x");

            Assert.Throws<InvalidOperationException>(() =>
                FixEngine.ApplyBrokenFixes(root, graph, analysis));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static RegistrationGraph ParseGraph(string root)
    {
        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            AllTargetFrameworks = false,
            TargetFramework = "net8.0"
        });
        return parser.ParseDirectory(root).SingleGraphOrDefault()
            ?? throw new InvalidOperationException("Expected one context graph.");
    }

    private static string CreateBrokenFixture(bool includeComplexDep)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-broken-fix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "DcsBrokenFixTest.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(root, "Services.cs"), """
            namespace DcsBrokenFixTest;

            public interface IDepService { }
            public sealed class DepServiceImpl : IDepService { }

            public interface IConsumer { }
            public sealed class ConsumerImpl : IConsumer
            {
                public ConsumerImpl(IDepService dep) { }
            }
            """);

        var depRegistration = includeComplexDep
            ? """
              services.TryAddSingleton<IDepService>(sp =>
                  new DepServiceImpl(sp.GetRequiredService<IServiceProvider>()));
              """
            : """
              services.TryAddSingleton<IDepService>(sp => new DepServiceImpl());
              """;

        File.WriteAllText(Path.Combine(root, "Registrations.cs"), $$"""
            using Microsoft.Extensions.DependencyInjection;

            namespace DcsBrokenFixTest;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    {{depRegistration}}
                    services.TryAddSingleton<IConsumer>(sp =>
                        new ConsumerImpl(sp.GetRequiredService<IDepService>()));
                }
            }
            """);

        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
