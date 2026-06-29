namespace DCS.Core.Parsing;

using DCS.Core.IR;

public sealed record ContextGraph
{
    public required string ContextId { get; init; }
    public required TypeRef EntryRoot { get; init; }
    public string ModuleId { get; init; } = "*";
    public SourceSetKind SourceSet { get; init; } = SourceSetKind.Main;
    public required RegistrationGraph Graph { get; init; }

    public static string BuildContextId(string moduleId, SourceSetKind sourceSet, string entryRootFqn) =>
        $"{moduleId}|{sourceSet.ToString().ToLowerInvariant()}|{entryRootFqn}";
}

public sealed record ParseResult
{
    public List<ContextGraph> ContextGraphs { get; init; } = [];
    public List<ParseDiagnostic> Diagnostics { get; init; } = [];

    public RegistrationGraph? SingleGraphOrDefault() =>
        ContextGraphs.Count == 1 ? ContextGraphs[0].Graph : null;
}
