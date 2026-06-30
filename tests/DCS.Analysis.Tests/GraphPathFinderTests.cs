using DCS.Core.IR;
using Xunit;

namespace DCS.Analysis.Tests;

public sealed class GraphPathFinderTests
{
    [Fact]
    public void Finds_shortest_dependency_path_from_root_to_target()
    {
        var root = MakeNode("IRoot", "CompositionRoot.cs", 1);
        var middle = MakeNode("IMiddle", "CompositionRoot.cs", 2);
        var target = MakeNode("ITarget", "CompositionRoot.cs", 3);

        var graph = new RegistrationGraph
        {
            Nodes = [root, middle, target],
            Edges =
            [
                MakeEdge(root, middle),
                MakeEdge(middle, target)
            ]
        };

        var result = GraphPathFinder.FindPath(graph, fromQuery: "IRoot", toQuery: "ITarget");
        Assert.True(result.Success);
        Assert.Equal(3, result.Nodes.Count);
        Assert.Equal(2, result.Edges.Count);
        Assert.Equal("IRoot", result.Nodes[0].DisplayName);
        Assert.Equal("ITarget", result.Nodes[^1].DisplayName);
    }

    [Fact]
    public void Uses_default_seeds_when_from_omitted()
    {
        var seed = MakeNode("ISeed", "CompositionRoot.cs", 10);
        var target = MakeNode("ITarget", "Services/Worker.cs", 20);
        var graph = new RegistrationGraph
        {
            Nodes = [seed, target],
            Edges = [MakeEdge(seed, target)]
        };

        var result = GraphPathFinder.FindPath(graph, fromQuery: null, toQuery: "ITarget");
        Assert.True(result.Success);
        Assert.Equal("ISeed", result.Nodes[0].DisplayName);
    }

    [Fact]
    public void Returns_single_node_when_target_is_seed()
    {
        var seed = MakeNode("VoiceCloneConsentCoordinator", "App.axaml.cs", 63);
        var graph = new RegistrationGraph { Nodes = [seed], Edges = [] };

        var result = GraphPathFinder.FindPath(graph, fromQuery: null, toQuery: "VoiceCloneConsentCoordinator");
        Assert.True(result.Success);
        Assert.Single(result.Nodes);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void Reports_ambiguous_short_name()
    {
        var a = MakeNode("IFoo", "A.cs", 1);
        var b = MakeNode("IFoo", "B.cs", 2);
        var graph = new RegistrationGraph { Nodes = [a, b], Edges = [] };

        var result = GraphPathFinder.FindPath(graph, fromQuery: null, toQuery: "IFoo");
        Assert.False(result.Success);
        Assert.True(result.IsAmbiguous);
    }

    [Fact]
    public void Reports_no_path_when_unreachable()
    {
        var a = MakeNode("IA", "A.cs", 1);
        var b = MakeNode("IB", "B.cs", 2);
        var graph = new RegistrationGraph { Nodes = [a, b], Edges = [] };

        var result = GraphPathFinder.FindPath(graph, fromQuery: "IA", toQuery: "IB");
        Assert.False(result.Success);
        Assert.Contains("No dependency path", result.Error, StringComparison.Ordinal);
    }

    private static RegistrationNode MakeNode(string shortName, string file, int line)
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId("test-scope", file, line, 0, line, 80, 0);
        return new()
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            DisplayName = shortName,
            AbstractToken = TypeRef.FromShortName(shortName),
            CompositionScopeId = "test-scope",
            SourceLocation = new SourceRef { FilePath = file, Line = line }
        };
    }

    private static DependencyEdge MakeEdge(RegistrationNode from, RegistrationNode to) => new()
    {
        Id = DependencyEdge.ComputeId(from.Id, to.Id),
        From = from.Id,
        To = to.Id
    };
}
