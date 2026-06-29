namespace DCS.Core.IR;

public sealed record ParseDiagnostic
{
    public required string Pattern { get; init; }
    public string? ContextId { get; init; }
    public string? Description { get; init; }
    public SourceRef? Location { get; init; }
}
