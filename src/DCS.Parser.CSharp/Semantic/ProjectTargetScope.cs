namespace DCS.Parser.CSharp.Semantic;

public sealed record ProjectTargetScope
{
    public required string ScopeId { get; init; }
    public required string CsprojPath { get; init; }
    public required string TargetFramework { get; init; }
    public string BuildConfiguration { get; init; } = "Debug";
    public required string AssemblyName { get; init; }
    public required string SourceMembershipProfileHash { get; init; }
    public IReadOnlyList<string> SourceFiles { get; init; } = [];
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
    public IReadOnlyList<string> PackageReferences { get; init; } = [];
    public IReadOnlyList<string> DefineConstants { get; init; } = [];
    public string? LangVersion { get; init; }
    public bool NullableEnabled { get; init; } = true;
    public bool ImplicitUsingsEnabled { get; init; } = true;
    public bool AllowUnsafeBlocks { get; init; }
    public bool ProjectEvaluationIncomplete { get; init; }
    public bool ImplicitUsingsUnmodeled { get; init; }
    public bool ProjectReferenceUnresolved { get; init; }
    public bool IsTestProject { get; init; }

    public string CompositionScopeId => ScopeId;
}
