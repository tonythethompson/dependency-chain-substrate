using DCS.Core.IR;
using DCS.Diff;
using DCS.Parser.CSharp;
using DCS.Verification;

namespace DCS.Diff.Tests;

public sealed class GraphDifferTests
{
    private static RegistrationNode MakeNode(string id, string name, Lifetime lifetime = Lifetime.Singleton) =>
        new()
        {
            Id = id,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            Lifetime = lifetime
        };

    private static RegistrationGraph Graph(params RegistrationNode[] nodes) =>
        new() { Nodes = [.. nodes], CommitSha = "abc" };

    [Fact]
    public void Empty_graphs_produce_empty_diff()
    {
        var diff = new GraphDiffer().Diff(new RegistrationGraph(), new RegistrationGraph());
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Added_node_detected()
    {
        var old = Graph();
        var newG = Graph(MakeNode("a1", "IFoo"));
        var diff = new GraphDiffer().Diff(old, newG);
        Assert.Single(diff.Added);
        Assert.Equal("IFoo", diff.Added.First().NewNode!.DisplayName);
    }

    [Fact]
    public void Removed_node_detected()
    {
        var old = Graph(MakeNode("a1", "IFoo"));
        var newG = Graph();
        var diff = new GraphDiffer().Diff(old, newG);
        Assert.Single(diff.Removed);
        Assert.Equal("IFoo", diff.Removed.First().OldNode!.DisplayName);
    }

    [Fact]
    public void Unchanged_node_produces_no_change()
    {
        var node = MakeNode("a1", "IFoo");
        var diff = new GraphDiffer().Diff(Graph(node), Graph(node));
        Assert.Empty(diff.NodeChanges);
    }

    [Fact]
    public void Lifetime_change_detected()
    {
        var old = Graph(MakeNode("a1", "IFoo", Lifetime.Singleton));
        var newG = Graph(MakeNode("a1", "IFoo", Lifetime.Transient));
        var diff = new GraphDiffer().Diff(old, newG);
        Assert.Single(diff.Modified);
        Assert.Equal(NodeChangeKind.LifetimeChanged, diff.Modified.First().Kind);
    }

    [Fact]
    public void Rename_detected_above_threshold()
    {
        // INavigationService → INavService (very similar names, no edges)
        var old = Graph(MakeNode("aaa1", "INavigationService"));
        // Different ID but similar name
        var newG = Graph(MakeNode("bbb1", "INavService"));
        var diff = new GraphDiffer().Diff(old, newG);

        // With name weight=0.5 and similarity of "INavigationService"/"INavService" ≈ 0.72 > 0.7 threshold
        // + lifetime match (both Unknown = 1.0) × 0.2 = 0.2
        // Total ≈ 0.5×0.72 + 0.3×1.0 + 0.2×1.0 = 0.36 + 0.3 + 0.2 = 0.86 (with empty dep Jaccard = 1.0)
        Assert.Single(diff.Renamed);
        Assert.Equal("INavigationService", diff.Renamed.First().OldNode!.DisplayName);
        Assert.Equal("INavService", diff.Renamed.First().NewNode!.DisplayName);
        Assert.True(diff.Renamed.First().SimilarityScore >= 0.7);
    }

    [Fact]
    public void Clearly_different_nodes_not_renamed()
    {
        var old = Graph(MakeNode("aaa1", "IWinUIButtonService", Lifetime.Singleton));
        var newG = Graph(MakeNode("bbb1", "IAvaloniaDataGridProvider", Lifetime.Transient));
        var diff = new GraphDiffer().Diff(old, newG);
        // Names too different + lifetime mismatch → score < 0.7
        Assert.Empty(diff.Renamed);
        Assert.Single(diff.Removed);
        Assert.Single(diff.Added);
    }

    [Fact]
    public void Edge_added_detected()
    {
        var nodeA = MakeNode("a1", "IFoo");
        var nodeB = MakeNode("b1", "IBar");
        var edge = new DependencyEdge { Id = "e1", From = "a1", To = "b1" };

        var old = new RegistrationGraph { Nodes = [nodeA, nodeB], Edges = [] };
        var newG = new RegistrationGraph { Nodes = [nodeA, nodeB], Edges = [edge] };

        var diff = new GraphDiffer().Diff(old, newG);
        Assert.Single(diff.EdgeChanges.Where(e => e.Kind == EdgeChangeKind.Added));
    }

    [Fact]
    public void Edge_removed_detected()
    {
        var nodeA = MakeNode("a1", "IFoo");
        var nodeB = MakeNode("b1", "IBar");
        var edge = new DependencyEdge { Id = "e1", From = "a1", To = "b1" };

        var old = new RegistrationGraph { Nodes = [nodeA, nodeB], Edges = [edge] };
        var newG = new RegistrationGraph { Nodes = [nodeA, nodeB], Edges = [] };

        var diff = new GraphDiffer().Diff(old, newG);
        Assert.Single(diff.EdgeChanges.Where(e => e.Kind == EdgeChangeKind.Removed));
    }

    [Fact]
    [Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
    [Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigration)]
    public void Trackdub_babel_to_trackdub_storage_paths_is_detected_as_rename()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
            return;

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            TargetFramework = "net10.0",
            IncludeTests = false,
            NoCache = true
        });
        var before = parser.ParseCommit(path, $"{TrackdubPin.BabelToTrackdubRenameCommit}^").ContextGraphs[0].Graph;
        var after = parser.ParseCommit(path, TrackdubPin.BabelToTrackdubRenameCommit).ContextGraphs[0].Graph;

        var diff = new GraphDiffer().Diff(before, after);

        var rename = Assert.Single(diff.Renamed.Where(r =>
            r.OldNode!.DisplayName == "BabelStudioStoragePaths" &&
            r.NewNode!.DisplayName == "TrackdubStoragePaths"));
        Assert.True(rename.SimilarityScore >= 0.7,
            $"Expected labelled Trackdub rename score above threshold; got {rename.SimilarityScore:F2}.");
    }
}
