using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Parser.Java;

public sealed record JavaParseOptions
{
    public FrameworkBoundaryModel Boundaries { get; init; } = FrameworkBoundaryModel.Default;
    public string? CacheDirectory { get; init; }
    public bool NoCache { get; init; }
    public Action<string>? OnCacheHit { get; init; }
    public IReadOnlyList<string>? ContextRoots { get; init; }
    public SourceSetFilter SourceSets { get; init; } = SourceSetFilter.MainOnly;
}
