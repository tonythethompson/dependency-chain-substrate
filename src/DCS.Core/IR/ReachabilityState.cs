namespace DCS.Core.IR;

/// <summary>
/// Static reachability within a declared Spring application context.
/// <see cref="ProvenActive"/> is reserved for future runtime-assisted import.
/// </summary>
public enum ReachabilityState
{
    StaticallyReachable,
    Candidate,
    Conditional,
    External,
    Unresolved,
    ProvenActive
}
