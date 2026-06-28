namespace DCS.Core.IR;

public sealed record BlindSpotReport
{
    public required string Pattern { get; init; }
    public SourceRef? Location { get; init; }
    public required string Description { get; init; }
}
