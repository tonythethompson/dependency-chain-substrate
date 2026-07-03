using DCS.Analysis;
using DCS.Core.IR;
using DCS.Runtime;

namespace DCS.Runtime.Tests;

public sealed class RuntimeGraphEnricherEdgeCasesTests
{
    [Fact]
    public void Enrich_handles_empty_graph_and_empty_events()
    {
        var graph = new RegistrationGraph { Nodes = [], Edges = [] };

        var report = RuntimeGraphEnricher.Enrich(graph, []);

        Assert.Empty(report.EnrichedGraph.Nodes);
        Assert.Equal(0, report.AnnotatedNodeCount);
        Assert.Equal(0, report.TotalResolutionEvents);
        Assert.Empty(report.CaptiveDependencies);
        Assert.Empty(report.RuntimeDiscoveredTypes);
    }

    [Fact]
    public void Enrich_leaves_unmatched_nodes_unannotated()
    {
        var node = MakeNode("IUnmatched", "unmatched", Confidence.Explicit);
        var graph = new RegistrationGraph { Nodes = [node], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "SomethingElse", ResolvedType = "Impl" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        var unchanged = report.EnrichedGraph.Nodes.Single();
        Assert.Empty(unchanged.Annotations);
        Assert.Equal(0, report.AnnotatedNodeCount);
    }

    [Fact]
    public void Enrich_does_not_upgrade_confidence_when_resolved_type_missing()
    {
        var node = MakeNode("IFactory", "factory", Confidence.BlindSpot);
        var graph = new RegistrationGraph { Nodes = [node], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "IFactory", ResolvedType = null }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        var annotated = report.EnrichedGraph.Nodes.Single();
        Assert.Equal(Confidence.BlindSpot, annotated.ParserConfidence);
        Assert.True(annotated.Annotations.ContainsKey(RuntimeGraphEnricher.RuntimeConfirmedKey));
        Assert.False(annotated.Annotations.ContainsKey(RuntimeGraphEnricher.ResolvedTypeKey));
    }

    [Fact]
    public void Enrich_does_not_upgrade_confidence_for_non_blind_spot_nodes()
    {
        var node = MakeNode("IExplicit", "explicit-node", Confidence.Explicit);
        var graph = new RegistrationGraph { Nodes = [node], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "IExplicit", ResolvedType = "ExplicitImpl" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        var annotated = report.EnrichedGraph.Nodes.Single();
        Assert.Equal(Confidence.Explicit, annotated.ParserConfidence);
    }

    [Fact]
    public void Enrich_matches_by_concrete_impl_type_name()
    {
        var node = new RegistrationNode
        {
            Id = "impl-match",
            RegistrationInstanceId = "impl-match",
            InstanceId = "impl-match",
            DisplayName = "IFoo -> FooImpl",
            AbstractToken = TypeRef.FromShortName("IFoo"),
            ConcreteImpl = TypeRef.FromShortName("FooImpl"),
            ParserConfidence = Confidence.Explicit
        };
        var graph = new RegistrationGraph { Nodes = [node], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "FooImpl", ResolvedType = "FooImpl" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Equal(1, report.AnnotatedNodeCount);
    }

    [Fact]
    public void Enrich_normalizes_global_prefix_and_generic_arguments()
    {
        var node = MakeNode("IRepository", "repo", Confidence.Explicit);
        var graph = new RegistrationGraph { Nodes = [node], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "global::IRepository<int>", ResolvedType = "Impl" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Equal(1, report.AnnotatedNodeCount);
    }

    [Fact]
    public void Enrich_is_case_insensitive_when_matching_type_names()
    {
        var node = MakeNode("IFoo", "foo", Confidence.Explicit);
        var graph = new RegistrationGraph { Nodes = [node], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "ifoo", ResolvedType = "FooImpl" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Equal(1, report.AnnotatedNodeCount);
    }

    [Fact]
    public void Enrich_does_not_reclassify_orphaned_when_no_static_analysis_provided()
    {
        var orphan = MakeNode("IOrphan", "orphan", Confidence.Explicit);
        var graph = new RegistrationGraph { Nodes = [orphan], Edges = [] };

        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "IOrphan", ResolvedType = "OrphanImpl" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events, staticAnalysis: null);

        Assert.Empty(report.OrphanedReclassifiedNodeIds);
    }

    [Fact]
    public void Enrich_ignores_events_with_blank_requested_type_for_discovery()
    {
        var graph = new RegistrationGraph { Nodes = [], Edges = [] };
        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "   ", ResolvedType = null, CallerType = null }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Empty(report.RuntimeDiscoveredTypes);
    }

    [Fact]
    public void Enrich_sets_metadata_runtime_event_count()
    {
        var graph = new RegistrationGraph { Nodes = [], Edges = [] };
        var events = new List<RuntimeResolutionEvent>
        {
            new() { RequestedType = "A" },
            new() { RequestedType = "B" }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Equal("2", report.EnrichedGraph.Metadata["runtime_event_count"]);
        Assert.Equal(2, report.TotalResolutionEvents);
    }

    [Fact]
    public void Enrich_no_captive_dependency_when_caller_lifetime_not_singleton()
    {
        var graph = new RegistrationGraph { Nodes = [], Edges = [] };
        var events = new List<RuntimeResolutionEvent>
        {
            new()
            {
                RequestedType = "IScopedService",
                ResolvedType = "ScopedService",
                Lifetime = "Scoped",
                CallerType = "ScopedHost",
                CallerLifetime = "Scoped"
            }
        };

        var report = RuntimeGraphEnricher.Enrich(graph, events);

        Assert.Empty(report.CaptiveDependencies);
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
