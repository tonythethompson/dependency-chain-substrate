using DCS.Analysis;
using DCS.Core.IR;
using DCS.Viz;

namespace DCS.Cli.Tests;

public sealed class HtmlVizPathHighlightTests
{
    [Fact]
    public void Generate_embeds_path_highlight_payload()
    {
        var root = MakeNode("IRoot", "root-1", "CompositionRoot.cs", 1);
        var mid = MakeNode("IMid", "mid-1", "CompositionRoot.cs", 2);
        var target = MakeNode("ITarget", "target-1", "Services/Target.cs", 3);
        var graph = new RegistrationGraph
        {
            ParserVersion = "test",
            Nodes = [root, mid, target],
            Edges = [MakeEdge(root, mid), MakeEdge(mid, target)]
        };

        var path = GraphPathFinder.FindPath(graph, fromQuery: "IRoot", toQuery: "ITarget");
        Assert.True(path.Success);

        var highlight = VizPathHighlight.FromResult(path);
        Assert.NotNull(highlight);

        var html = HtmlVizGenerator.Generate(graph, analysis: null, highlight);

        Assert.Contains("PATH_HIGHLIGHT", html);
        Assert.Contains("root-1", html);
        Assert.Contains("target-1", html);
        Assert.Contains("root-1|mid-1", html);
        Assert.Contains("Path highlight", html);
    }

    [Fact]
    public void Generate_without_path_highlight_uses_null_payload()
    {
        var graph = new RegistrationGraph
        {
            ParserVersion = "test",
            Nodes = [MakeNode("IFoo", "foo-1", "Foo.cs", 1)]
        };

        var html = HtmlVizGenerator.Generate(graph);

        Assert.Contains("const PATH_HIGHLIGHT = null;", html);
    }

    [Fact]
    public void Generate_large_graph_smoke_test_embeds_all_nodes_and_scale_metadata()
    {
        var nodes = Enumerable.Range(0, 1_200)
            .Select(i => MakeNode($"IService{i}", $"node-{i}", $"Services/Service{i}.cs", i + 1))
            .ToList();
        var edges = Enumerable.Range(0, 1_199)
            .Select(i => MakeEdge(nodes[i], nodes[i + 1]))
            .ToList();
        var graph = new RegistrationGraph
        {
            ParserVersion = "scale-test",
            Nodes = nodes,
            Edges = edges
        };

        var html = HtmlVizGenerator.Generate(graph);

        Assert.Contains("IService1199", html);
        Assert.Contains("node-1199", html);
        Assert.Contains("Large graph", html);
    }

    private static RegistrationNode MakeNode(string name, string id, string file, int line) =>
        new()
        {
            Id = id,
            RegistrationInstanceId = id,
            InstanceId = id,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            ParserConfidence = Confidence.Explicit,
            SourceLocation = new SourceRef { FilePath = file, Line = line }
        };

    private static DependencyEdge MakeEdge(RegistrationNode from, RegistrationNode to) => new()
    {
        Id = DependencyEdge.ComputeId(from.Id, to.Id),
        From = from.Id,
        To = to.Id
    };
}
