namespace DCS.Core.IR;

public sealed record ContextMembership
{
    public required string ContextId { get; init; }
    public ReachabilityState State { get; init; } = ReachabilityState.Candidate;
    public MembershipEvidence Evidence { get; init; } = MembershipEvidence.ComponentScan;
}
