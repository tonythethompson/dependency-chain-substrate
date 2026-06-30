namespace DCS.Analysis;

public enum FindingTier
{
    Actionable,
    Informational,
    ParserLimit,
    Intentional
}

public enum FindingCategory
{
    Leaked,
    Broken,
    Duplicate,
    PossibleDuplicate,
    Unresolved,
    Orphaned,
    Cycle,
    BlindSpot
}

public enum FindingSeverity
{
    Error,
    Warn
}

public enum ReportVerbosity
{
    Summary,
    Actionable,
    Full
}

public sealed record RegistrationSite
{
    public required string RegistrationId { get; init; }
    public required string DisplayName { get; init; }
    public string? FullyQualifiedName { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public IReadOnlyList<string> FrameworkTags { get; init; } = [];
}

public sealed record AnalysisFinding
{
    public required string FindingId { get; init; }
    public required FindingCategory Category { get; init; }
    public required FindingSeverity Severity { get; init; }
    public required FindingTier Tier { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<RegistrationSite> Sites { get; init; } = [];
}

public sealed record ExtractionQualityMetrics
{
    public double SemanticTypeResolutionRate { get; init; }
    public double RegistrationApiVerificationRate { get; init; }
    public double ProjectScopeCompletenessRate { get; init; }
    public int TotalRegistrations { get; init; }
    public int ResolvedRegistrations { get; init; }
    public int VerifiedRegistrations { get; init; }
    public int SyntaxCandidateRegistrations { get; init; }
    public int CompositionScopeCount { get; init; }
    public int CompleteCompositionScopeCount { get; init; }
}

public sealed record AnalysisReportSummary
{
    public int ActionableCount { get; init; }
    public int InformationalCount { get; init; }
    public int ParserLimitCount { get; init; }
    public int IntentionalCount { get; init; }
    public int ErrorCount { get; init; }
    public int LeakedCount { get; init; }
    public int BrokenCount { get; init; }
    public int DuplicateCount { get; init; }
    public int PossibleDuplicateCount { get; init; }
    public int UnresolvedCount { get; init; }
    public int OrphanedCount { get; init; }
    public int CycleCount { get; init; }
    public int BlindSpotCount { get; init; }
    public bool HasErrors { get; init; }
}

public sealed record AnalysisReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string? CommitSha { get; init; }
    public string? ContextId { get; init; }
    public string? TargetFramework { get; init; }
    public string? ParserVersion { get; init; }
    public int TotalNodes { get; init; }
    public int TotalEdges { get; init; }
    public ExtractionQualityMetrics? Metrics { get; init; }
    public AnalysisReportSummary Summary { get; init; } = new();
    public IReadOnlyList<AnalysisFinding> Findings { get; init; } = [];
    public IReadOnlyList<string> AvailableContexts { get; init; } = [];
}

public sealed record MultiContextAnalysisReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string? CommitSha { get; init; }
    public IReadOnlyList<AnalysisReport> ContextReports { get; init; } = [];
}
