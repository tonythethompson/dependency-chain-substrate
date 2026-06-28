using DCS.Analysis;
using DCS.Core.IR;
using Xunit;

namespace DCS.Analysis.Tests;

public sealed class GraphAnalyzerTests
{
    private static RegistrationNode MakeNode(string shortName, string[] frameworks = null!, Confidence confidence = Confidence.Explicit) =>
        new()
        {
            Id = RegistrationNode.ComputeId(shortName),
            DisplayName = shortName,
            AbstractToken = TypeRef.FromShortName(shortName),
            ParserConfidence = confidence,
            FrameworkTags = [.. (frameworks ?? [])]
        };

    private static DependencyEdge MakeEdge(string from, string to) => new()
    {
        Id = DependencyEdge.ComputeId(
            RegistrationNode.ComputeId(from),
            RegistrationNode.ComputeId(to)),
        From = RegistrationNode.ComputeId(from),
        To = RegistrationNode.ComputeId(to)
    };

    [Fact]
    public void Detects_orphaned_node_with_no_incoming_edges()
    {
        var graph = new RegistrationGraph
        {
            Nodes = [MakeNode("IFoo"), MakeNode("IBar")],
            Edges = [] // no edges — both orphaned (except root)
        };
        var result = new GraphAnalyzer(graph).Analyze();
        // At least one of them is orphaned (the non-root)
        Assert.True(result.Orphaned.Count >= 1);
    }

    [Fact]
    public void Detects_duplicate_abstract_tokens()
    {
        var node1 = MakeNode("IFoo") with { FrameworkTags = ["winui"] };
        var node2 = new RegistrationNode
        {
            Id = RegistrationNode.ComputeId("IFoo_avalonia"), // different ID
            DisplayName = "IFoo",
            AbstractToken = TypeRef.FromShortName("IFoo"), // same short name!
            FrameworkTags = ["avalonia"],
            ParserConfidence = Confidence.Explicit
        };
        var graph = new RegistrationGraph { Nodes = [node1, node2] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Single(result.Duplicates);
        Assert.Equal("IFoo", result.Duplicates[0].AbstractTokenName);
    }

    [Fact]
    public void Detects_cross_framework_leaked_edge()
    {
        var winuiNode = MakeNode("IWinUIService", ["winui"]);
        var avaloniaNode = MakeNode("IAvaloniaConsumer", ["avalonia"]);
        var edge = new DependencyEdge
        {
            Id = "test-edge",
            From = avaloniaNode.Id,
            To = winuiNode.Id
        };
        var graph = new RegistrationGraph
        {
            Nodes = [winuiNode, avaloniaNode],
            Edges = [edge]
        };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Single(result.Leaked);
        Assert.Equal("IAvaloniaConsumer", result.Leaked[0].DisplayName);
    }

    [Fact]
    public void No_false_positive_leakage_for_same_framework_edge()
    {
        var nodeA = MakeNode("IFoo", ["avalonia"]);
        var nodeB = MakeNode("IBar", ["avalonia"]);
        var edge = new DependencyEdge
        {
            Id = "edge",
            From = nodeA.Id,
            To = nodeB.Id
        };
        var graph = new RegistrationGraph { Nodes = [nodeA, nodeB], Edges = [edge] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Empty(result.Leaked);
    }

    [Fact]
    public void Broken_chain_reported_for_blind_spot_dependency()
    {
        var consumer = MakeNode("IConsumer") with { ConcreteImpl = TypeRef.FromShortName("ConsumerImpl") };
        var blindSpot = MakeNode("IFactory", confidence: Confidence.BlindSpot);
        var edge = new DependencyEdge
        {
            Id = "e1",
            From = consumer.Id,
            To = blindSpot.Id
        };
        var graph = new RegistrationGraph { Nodes = [consumer, blindSpot], Edges = [edge] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Single(result.BrokenChains);
        Assert.Equal("IConsumer", result.BrokenChains[0].DisplayName);
    }
}
