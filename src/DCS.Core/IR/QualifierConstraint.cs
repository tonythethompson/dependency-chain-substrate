namespace DCS.Core.IR;

public sealed record QualifierConstraint
{
    public required string Kind { get; init; }
    public required string Value { get; init; }
}
