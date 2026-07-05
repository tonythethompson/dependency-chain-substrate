namespace DCS.Analysis;

public sealed class AnalysisResult
{
    public List<LeakedRegistration> Leaked { get; init; } = [];
    public List<OrphanedRegistration> Orphaned { get; init; } = [];
    public List<OrphanedRegistration> IslandValidOrphans { get; init; } = [];
    public List<BrokenChain> BrokenChains { get; init; } = [];
    public List<DuplicateAbstractToken> Duplicates { get; init; } = [];
    public List<DuplicateAbstractToken> PossibleDuplicates { get; init; } = [];
    public List<List<string>> Cycles { get; init; } = [];
    public string? CompositionRootId { get; init; }
    public int TotalNodes { get; init; }
    public int TotalEdges { get; init; }
    public int TotalBlindSpots { get; init; }
    public int TotalUnresolvedInjections { get; init; }
    public IReadOnlyList<CompositionIslandSummary> IslandSummaries { get; init; } = [];
    public bool HasErrors => Leaked.Count > 0 || BrokenChains.Count > 0;
}

public sealed record CompositionIslandSummary
{
    public required CompositionIsland Island { get; init; }
    public int SeedCount { get; init; }
    public int ReachableCount { get; init; }
    public int OrphanedCount { get; init; }
    public int IslandValidCount { get; init; }
    public int TrueOrphanCount { get; init; }
}

public record LeakedRegistration(
    string NodeId,
    string DisplayName,
    string FromFramework,
    string ToFramework,
    string? SourceFile,
    int? SourceLine);

public record OrphanedRegistration(
    string NodeId,
    string DisplayName,
    string? SourceFile,
    int? SourceLine,
    CompositionIsland Island = CompositionIsland.Unknown,
    bool IsIslandValid = false);

public record BrokenChain(
    string NodeId,
    string DisplayName,
    string MissingDependencyType,
    string? SourceFile,
    int? SourceLine);

public record DuplicateAbstractToken(
    string AbstractTokenName,
    IReadOnlyList<string> NodeIds,
    bool IsStrict = true);
