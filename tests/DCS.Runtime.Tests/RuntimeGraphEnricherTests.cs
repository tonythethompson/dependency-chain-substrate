using DCS.Analysis;
using DCS.Core.IR;
using DCS.Runtime;

namespace DCS.Runtime.Tests;

public sealed class RuntimeGraphEnricherTests
{
    [Fact]
    public void Enrich_annotates_matching_nodes_and_upgrades_blind_spot()
    {
        var blind = MakeNode("IFactory", "factory", Confidence.BlindSpot);
        var explicitNode = MakeNode("IAppService", "app", Confidence.Explicit);
        var graph = new RegistrationGraph
        {
            ParserVersion = "test",
            Nodes = [blind, explicitNode],
            Edges = []
        };

        var events = new List<RuntimeResolutionEvent>
        {
            new()
            {
                RequestedType = "IFactory",
                ResolvedType = "FactoryImpl",
                Lifetime = "Singleton"
            },
            new()
            {
                RequestedType = "IAppService",
                ResolvedType = "AppService",
                Lifetime = "Scoped"
            },
            new()
            {
                RequestedType = "IAppService",
                ResolvedType = "AppService",
                Lifetime = "Scoped"
            }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Equal("true", report.EnrichedGraph.Metadata["runtime_enriched"]);
        Assert.Equal(2, report.AnnotatedNodeCount);
        Assert.Single(report.BlindSpotConfirmedNodeIds);

        var upgraded = report.EnrichedGraph.Nodes.Single(n => n.Id == "factory");
        Assert.Equal(Confidence.Inferred, upgraded.ParserConfidence);
        Assert.Equal("1", upgraded.Annotations[RuntimeGraphEnricher.ResolvedCountKey]);
        Assert.Equal("FactoryImpl", upgraded.Annotations[RuntimeGraphEnricher.ResolvedTypeKey]);

        var app = report.EnrichedGraph.Nodes.Single(n => n.Id == "app");
        Assert.Equal("2", app.Annotations[RuntimeGraphEnricher.ResolvedCountKey]);
    }

    [Fact]
    public void Enrich_reclassifies_orphaned_when_static_analysis_matches()
    {
        var orphan = MakeNode("IOrphan", "orphan", Confidence.Explicit);
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

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "IOrphan", ResolvedType = "OrphanImpl", Lifetime = "Singleton" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events, analysis);

        Assert.Single(report.OrphanedReclassifiedNodeIds);
        Assert.Equal("orphan", report.OrphanedReclassifiedNodeIds[0]);
    }

    [Fact]
    public void Enrich_detects_captive_dependency()
    {
        var graph = new RegistrationGraph { ParserVersion = "test", Nodes = [], Edges = [] };
        var events = new List<RuntimeResolutionEvent>
        {
            new()
            {
                RequestedType = "IScopedService",
                ResolvedType = "ScopedService",
                Lifetime = "Scoped",
                CallerType = "SingletonHost",
                CallerLifetime = "Singleton"
            }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Single(report.CaptiveDependencies);
        Assert.Equal("IScopedService", report.CaptiveDependencies[0].ScopedServiceType);
        Assert.Equal("SingletonHost", report.CaptiveDependencies[0].CaptiveSingletonType);
    }

    [Fact]
    public void Enrich_lists_runtime_discovered_types()
    {
        var graph = new RegistrationGraph
        {
            ParserVersion = "test",
            Nodes = [MakeNode("IKnown", "known", Confidence.Explicit)],
            Edges = []
        };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "IKnown", ResolvedType = "KnownImpl" },
            new() { RequestedType = "IOnlyAtRuntime", ResolvedType = "RuntimeImpl", CallerType = "Host" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Contains("IOnlyAtRuntime", report.RuntimeDiscoveredTypes);
        Assert.Contains("RuntimeImpl", report.RuntimeDiscoveredTypes);
        Assert.Contains("Host", report.RuntimeDiscoveredTypes);
        Assert.DoesNotContain("IKnown", report.RuntimeDiscoveredTypes);
    }

    private static RegistrationNode MakeNode(string name, string id, Confidence confidence) =>
        new()
        {
            Id = id,
            RegistrationInstanceId = id,
            InstanceId = id,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            ParserConfidence = confidence
        };
}
