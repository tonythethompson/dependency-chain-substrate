namespace DCS.Core.IR;

public sealed record SourceRef
{
    public required string FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}
