namespace DCS.Fix;

public sealed record BuildVerificationResult(
    bool Succeeded,
    int ExitCode,
    string Output);
