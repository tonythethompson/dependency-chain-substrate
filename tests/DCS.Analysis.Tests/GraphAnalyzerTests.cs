using DCS.Analysis;
using DCS.Core.IR;
using Xunit;

namespace DCS.Analysis.Tests;

public sealed class GraphAnalyzerTests
{
    private static RegistrationNode MakeNode(string shortName, string file = "Test.cs", int line = 1, string[]? frameworks = null, Confidence confidence = Confidence.Explicit)
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId("test-scope", file, line, 0, line, 80, 0);
        return new RegistrationNode
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            DisplayName = shortName,
            AbstractToken = TypeRef.FromShortName(shortName),
            ParserConfidence = confidence,
            FrameworkTags = [.. (frameworks ?? [])],
            CompositionScopeId = "test-scope",
            SourceLocation = new SourceRef { FilePath = file, Line = line }
        };
    }

    private static RegistrationNode MakeStrictNode(
        string shortName,
        string file,
        int line,
        string[]? frameworks = null,
        string scopeId = "test-scope")
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId(scopeId, file, line, 0, line, 80, 0);
        var serviceType = ServiceTypeIdentity.FromResolved(new ResolvedTypeIdentity
        {
            AssemblyKey = AssemblyKey.FromProjectScope(scopeId),
            MetadataName = $"Test.{shortName}"
        });
        var dupKey = RegistrationNode.ComputeDuplicateGroupKey(scopeId, serviceType);
        return new RegistrationNode
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            ServiceType = serviceType,
            DuplicateGroupKey = dupKey,
            CompositionScopeId = scopeId,
            TypeResolutionQuality = TypeResolutionQuality.Resolved,
            RegistrationRecognitionQuality = RegistrationRecognitionQuality.VerifiedMicrosoftDI,
            DisplayName = shortName,
            AbstractToken = TypeRef.FromQualifiedName($"Test.{shortName}"),
            FrameworkTags = [.. (frameworks ?? [])],
            SourceLocation = new SourceRef { FilePath = file, Line = line },
            Annotations = new Dictionary<string, string>
            {
                [StrictDuplicateEligibility.AnnotationKey] = "true"
            }
        };
    }

    private static DependencyEdge MakeEdge(RegistrationNode from, RegistrationNode to) => new()
    {
        Id = DependencyEdge.ComputeId(from.Id, to.Id),
        From = from.Id,
        To = to.Id
    };

    [Fact]
    public void Detects_orphaned_node_with_no_incoming_edges()
    {
        var graph = new RegistrationGraph
        {
            Nodes = [MakeNode("IFoo"), MakeNode("IBar", "Other.cs", 2)],
            Edges = []
        };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.True(result.Orphaned.Count >= 1);
    }

    [Fact]
    public void Detects_strict_duplicate_registrations()
    {
        var node1 = MakeStrictNode("IFoo", "WinUI/App.xaml.cs", 10, ["winui"]);
        var node2 = MakeStrictNode("IFoo", "Avalonia/App.axaml.cs", 15, ["avalonia"]);
        var graph = new RegistrationGraph { Nodes = [node1, node2] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Single(result.Duplicates);
        Assert.Equal("IFoo", result.Duplicates[0].AbstractTokenName);
        Assert.True(result.Duplicates[0].IsStrict);
    }

    [Fact]
    public void Homonym_syntactic_nodes_are_possible_duplicate_not_strict()
    {
        var node1 = MakeNode("IFoo", "A.cs", 1);
        var node2 = MakeNode("IFoo", "B.cs", 5);
        var graph = new RegistrationGraph { Nodes = [node1, node2] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Empty(result.Duplicates);
        Assert.Single(result.PossibleDuplicates);
    }

    [Fact]
    public void Detects_cross_framework_leaked_edge()
    {
        var winuiNode = MakeNode("IWinUIService", frameworks: ["winui"]);
        var avaloniaNode = MakeNode("IAvaloniaConsumer", file: "Consumer.cs", line: 2, frameworks: ["avalonia"]);
        var graph = new RegistrationGraph
        {
            Nodes = [winuiNode, avaloniaNode],
            Edges = [MakeEdge(avaloniaNode, winuiNode)]
        };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Single(result.Leaked);
        Assert.Equal("IAvaloniaConsumer", result.Leaked[0].DisplayName);
    }

    [Fact]
    public void No_false_positive_leakage_for_same_framework_edge()
    {
        var nodeA = MakeNode("IFoo", frameworks: ["avalonia"]);
        var nodeB = MakeNode("IBar", file: "B.cs", line: 2, frameworks: ["avalonia"]);
        var graph = new RegistrationGraph { Nodes = [nodeA, nodeB], Edges = [MakeEdge(nodeA, nodeB)] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Empty(result.Leaked);
    }

    [Fact]
    public void Detects_cross_framework_leaked_via_duplicate_registration()
    {
        var winuiNode = MakeStrictNode("INavigationService", "App.xaml.cs", 10, ["winui"]);
        var avaloniaNode = MakeStrictNode("INavigationService", "App.axaml.cs", 15, ["avalonia"]);
        var graph = new RegistrationGraph { Nodes = [winuiNode, avaloniaNode] };
        var result = new GraphAnalyzer(graph).Analyze();

        Assert.Single(result.Leaked);
        Assert.Equal("INavigationService", result.Leaked[0].DisplayName);
    }

    [Fact]
    public void No_false_positive_for_same_framework_duplicate_registration()
    {
        var a = MakeStrictNode("IFoo", "A.cs", 1, ["avalonia"]);
        var b = MakeStrictNode("IFoo", "B.cs", 5, ["avalonia"]);
        var graph = new RegistrationGraph { Nodes = [a, b] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Empty(result.Leaked);
    }

    [Fact]
    public void Broken_chain_reported_for_blind_spot_dependency()
    {
        var consumer = MakeNode("IConsumer") with { ConcreteImpl = TypeRef.FromShortName("ConsumerImpl") };
        var blindSpot = MakeNode("IFactory", file: "Factory.cs", line: 2) with { ParserConfidence = Confidence.BlindSpot };
        var graph = new RegistrationGraph { Nodes = [consumer, blindSpot], Edges = [MakeEdge(consumer, blindSpot)] };
        var result = new GraphAnalyzer(graph).Analyze();
        Assert.Single(result.BrokenChains);
        Assert.Equal("IConsumer", result.BrokenChains[0].DisplayName);
    }
}
