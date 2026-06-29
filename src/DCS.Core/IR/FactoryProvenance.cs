namespace DCS.Core.IR;

public sealed record FactoryProvenance
{
    public required string ProductRegistrationId { get; init; }
    public required string OwnerTypeFqn { get; init; }
    public string? OwnerRegistrationId { get; init; }
    public required string FactoryMethod { get; init; }
    public FactoryInvocationMode InvocationMode { get; init; } = FactoryInvocationMode.Instance;
}
