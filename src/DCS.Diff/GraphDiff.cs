using DCS.Core.IR;

namespace DCS.Diff;

public enum NodeChangeKind
{
    Added,
    Removed,
    Renamed,
    LifetimeChanged,
    ImplementationChanged,
    ConfidenceChanged,
    FrameworkTagsChanged
}

public enum EdgeChangeKind { Added, Removed }

public sealed record NodeChange(
    NodeChangeKind Kind,
    RegistrationNode? OldNode,
    RegistrationNode? NewNode,
    double SimilarityScore = 1.0)
{
    public string DisplayName =>
        NewNode?.DisplayName ?? OldNode?.DisplayName ?? "(unknown)";
}

public sealed record EdgeChange(
    EdgeChangeKind Kind,
    DependencyEdge? OldEdge,
    DependencyEdge? NewEdge);

public sealed record GraphDiff
{
    public string? OldCommit { get; init; }
    public string? NewCommit { get; init; }
    public List<NodeChange> NodeChanges { get; init; } = [];
    public List<EdgeChange> EdgeChanges { get; init; } = [];

    public IEnumerable<NodeChange> Added =>
        NodeChanges.Where(c => c.Kind == NodeChangeKind.Added);
    public IEnumerable<NodeChange> Removed =>
        NodeChanges.Where(c => c.Kind == NodeChangeKind.Removed);
    public IEnumerable<NodeChange> Renamed =>
        NodeChanges.Where(c => c.Kind == NodeChangeKind.Renamed);
    public IEnumerable<NodeChange> Modified =>
        NodeChanges.Where(c => c.Kind is not NodeChangeKind.Added
            and not NodeChangeKind.Removed and not NodeChangeKind.Renamed);

    public bool IsEmpty => NodeChanges.Count == 0 && EdgeChanges.Count == 0;

    public bool HasBreakingChanges =>
        NodeChanges.Any(c => c.Kind == NodeChangeKind.Removed) ||
        EdgeChanges.Any(e => e.Kind == EdgeChangeKind.Removed);
}
