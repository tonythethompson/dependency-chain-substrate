using DCS.Analysis;

namespace DCS.Parser.CSharp;

public sealed record CSharpParseOptions
{
    public FrameworkBoundaryModel Boundaries { get; init; } = FrameworkBoundaryModel.Default;
    public string? CacheDirectory { get; init; }
    public bool NoCache { get; init; }
    public Action<string>? OnCacheHit { get; init; }
}
