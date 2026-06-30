using DCS.Analysis;
using DCS.Core.IR;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigration)]
public sealed class TrackdubPathGateTests
{
    [Fact]
    public void Trackdub_path_to_avalonia_voice_clone_succeeds()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
            return;

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            AllTargetFrameworks = true,
            IncludeTests = false
        });
        var result = parser.ParseCommit(path, TrackdubPin.CommitSha);
        var graph = new RegistrationGraph
        {
            ParserVersion = CSharpStaticParser.ParserVersion,
            CommitSha = TrackdubPin.CommitSha,
            SourceLanguage = "csharp",
            Nodes = result.ContextGraphs.SelectMany(c => c.Graph.Nodes).ToList(),
            Edges = result.ContextGraphs.SelectMany(c => c.Graph.Edges).ToList(),
            BlindSpots = result.ContextGraphs.SelectMany(c => c.Graph.BlindSpots).ToList(),
            UnresolvedInjections = result.ContextGraphs.SelectMany(c => c.Graph.UnresolvedInjections).ToList()
        };

        var avaloniaTarget = graph.Nodes.FirstOrDefault(n =>
            n.DisplayName.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal) &&
            n.SourceLocation?.FilePath?.Contains("trackdub.app.avalonia/app.axaml.cs", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(avaloniaTarget);

        var pathResult = GraphPathFinder.FindPath(graph, fromQuery: null, toQuery: avaloniaTarget!.Id);
        Assert.True(pathResult.Success, pathResult.Error);
        Assert.NotEmpty(pathResult.Nodes);
        Assert.Equal(avaloniaTarget.Id, pathResult.ToNodeId);
    }

    [Fact]
    public void Trackdub_finds_dependency_path_when_factory_edges_exist()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
            return;

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            TargetFramework = "net10.0",
            IncludeTests = false
        });
        var graph = parser.ParseCommit(path, TrackdubPin.CommitSha).ContextGraphs[0].Graph;
        if (graph.Edges.Count == 0)
            return;

        var edge = graph.Edges[0];
        var fromNode = graph.Nodes.First(n => n.Id == edge.From);
        var toNode = graph.Nodes.First(n => n.Id == edge.To);

        var pathResult = GraphPathFinder.FindPath(graph, fromQuery: fromNode.DisplayName, toQuery: toNode.DisplayName);
        if (pathResult.IsAmbiguous)
            return;

        Assert.True(pathResult.Success, pathResult.Error);
        Assert.True(pathResult.Nodes.Count >= 2);
    }
}
