using DCS.Analysis;
using DCS.Core.IR;
using DCS.Fix;
using DCS.Parser.CSharp;
using DCS.Verification;

namespace DCS.Fix.Tests;

public sealed class OrphanedFixPlannerTests
{
    [Fact]
    public void Plan_skips_composition_root()
    {
        var root = MakeNode("IAppHost", "root", "Program.cs", 10, Confidence.Explicit);
        var orphan = MakeNode("IOrphan", "orphan", "Orphans.cs", 5, Confidence.Explicit);
        var graph = new RegistrationGraph
        {
            ParserVersion = "test",
            Nodes = [root, orphan],
            Edges = []
        };

        var analysis = new AnalysisResult
        {
            CompositionRootId = root.Id,
            Orphaned =
            [
                new OrphanedRegistration(orphan.Id, orphan.DisplayName, "Orphans.cs", 5),
                new OrphanedRegistration(root.Id, root.DisplayName, "Program.cs", 10)
            ]
        };

        var proposals = OrphanedFixPlanner.Plan(graph, analysis);
        Assert.Single(proposals);
        Assert.Equal("IOrphan", proposals[0].DisplayName);
    }

    [Fact]
    public void Plan_skips_non_explicit_orphans()
    {
        var orphan = MakeNode("IOrphan", "orphan", "Orphans.cs", 5, Confidence.Inferred);
        var graph = new RegistrationGraph
        {
            ParserVersion = "test",
            Nodes = [orphan],
            Edges = []
        };

        var analysis = new AnalysisResult
        {
            Orphaned = [new OrphanedRegistration(orphan.Id, orphan.DisplayName, "Orphans.cs", 5)]
        };

        Assert.Empty(OrphanedFixPlanner.Plan(graph, analysis));
    }

    private static RegistrationNode MakeNode(
        string name,
        string id,
        string file,
        int line,
        Confidence confidence) =>
        new()
        {
            Id = id,
            RegistrationInstanceId = id,
            InstanceId = id,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            SourceLocation = new SourceRef { FilePath = file, Line = line },
            ParserConfidence = confidence
        };
}

public sealed class OrphanedFixMeasurementTests
{
    [Fact]
    public void Measure_reports_eligible_orphan_fixture()
    {
        var root = CreateOrphanFixture();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()
                ?? throw new InvalidOperationException("Expected one context graph.");
            var analysis = new GraphAnalyzer(graph).Analyze();

            Assert.Contains(analysis.Orphaned, o => o.DisplayName.Contains("IOrphanService", StringComparison.Ordinal));

            var measurement = OrphanedFixMeasurement.Measure(graph, analysis);
            Assert.True(measurement.TotalOrphaned >= 1);
            Assert.True(measurement.EligibleForFixPreview >= 1);
            Assert.Contains(measurement.EligibleOrphans, o => o.DisplayName.Contains("IOrphanService", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Preview_builds_unified_diff_for_orphan()
    {
        var root = CreateOrphanFixture();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var analysis = new GraphAnalyzer(graph).Analyze();
            var measurement = OrphanedFixMeasurement.Measure(graph, analysis);
            var result = FixEngine.BuildOrphanedFixes(root, graph, analysis, displayNameFilter: "IOrphanService");
            var preview = FixEngine.FormatOrphanedPreview(result, measurement);

            Assert.Contains("FIX ORPHANED", preview);
            Assert.Contains("IOrphanService", preview);
            Assert.DoesNotContain("IUsedService", preview);
            Assert.False(result.Applied);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_removes_orphan_and_analyze_shows_one_fewer_eligible()
    {
        var root = CreateOrphanFixture();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var before = new GraphAnalyzer(graph).Analyze();
            var beforeMeasurement = OrphanedFixMeasurement.Measure(graph, before);
            Assert.True(beforeMeasurement.EligibleForFixPreview >= 1);

            var applied = FixEngine.ApplyOrphanedFixes(
                root, graph, before, displayNameFilter: "IOrphanService", forceDirtyTree: true);
            Assert.True(applied.Applied);
            Assert.Single(applied.Proposals);

            var afterGraph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var afterMeasurement = OrphanedFixMeasurement.Measure(
                afterGraph, new GraphAnalyzer(afterGraph).Analyze());
            Assert.Equal(beforeMeasurement.EligibleForFixPreview - 1, afterMeasurement.EligibleForFixPreview);
            Assert.DoesNotContain(
                afterMeasurement.EligibleOrphans,
                o => o.DisplayName.Contains("IOrphanService", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_throws_when_working_tree_dirty()
    {
        var root = CreateOrphanFixture();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var analysis = new GraphAnalyzer(graph).Analyze();

            File.WriteAllText(Path.Combine(root, "dirty-marker.txt"), "x");

            Assert.Throws<InvalidOperationException>(() =>
                FixEngine.ApplyOrphanedFixes(root, graph, analysis, forceDirtyTree: false));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Trackdub_orphaned_measurement_when_available()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
            return;

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            AllTargetFrameworks = true,
            IncludeTests = false
        });
        var parseResult = parser.ParseCommit(path, TrackdubPin.CommitSha);
        var graph = new RegistrationGraph
        {
            ParserVersion = CSharpStaticParser.ParserVersion,
            CommitSha = TrackdubPin.CommitSha,
            SourceLanguage = "csharp",
            Nodes = parseResult.ContextGraphs.SelectMany(c => c.Graph.Nodes).ToList(),
            Edges = parseResult.ContextGraphs.SelectMany(c => c.Graph.Edges).ToList()
        };

        var measurement = OrphanedFixMeasurement.Measure(graph, new GraphAnalyzer(graph).Analyze());
        Assert.True(measurement.TotalOrphaned >= 0);
        Assert.True(measurement.EligibleForFixPreview <= measurement.TotalOrphaned);
    }

    private static string CreateOrphanFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-orphan-fix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "OrphanFixTest.csproj"), """
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
            namespace OrphanFixTest;
            public interface IUsedService { }
            public class UsedService : IUsedService { }
            public interface IOrphanService { }
            public class OrphanService : IOrphanService { }
            public interface IConsumer { }
            public class Consumer : IConsumer
            {
                public Consumer(IUsedService used) { }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Program.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using OrphanFixTest;

            var services = new ServiceCollection();
            services.AddSingleton<IUsedService, UsedService>();
            services.AddSingleton<IConsumer, Consumer>();
            """);

        File.WriteAllText(Path.Combine(root, "OrphanRegistrations.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using OrphanFixTest;

            namespace OrphanFixTest;

            public static class OrphanRegistrations
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddSingleton<IOrphanService, OrphanService>();
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
