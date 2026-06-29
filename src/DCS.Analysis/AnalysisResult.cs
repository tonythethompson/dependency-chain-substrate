namespace DCS.Analysis;

public sealed class AnalysisResult
{
    public List<LeakedRegistration> Leaked { get; init; } = [];
    public List<OrphanedRegistration> Orphaned { get; init; } = [];
    public List<BrokenChain> BrokenChains { get; init; } = [];
    public List<DuplicateAbstractToken> Duplicates { get; init; } = [];
    public List<DuplicateAbstractToken> PossibleDuplicates { get; init; } = [];
    public List<List<string>> Cycles { get; init; } = [];
    public string? CompositionRootId { get; init; }
    public int TotalNodes { get; init; }
    public int TotalEdges { get; init; }
    public int TotalBlindSpots { get; init; }
    public int TotalUnresolvedInjections { get; init; }
    public bool HasErrors => Leaked.Count > 0 || BrokenChains.Count > 0;
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
    int? SourceLine);

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
