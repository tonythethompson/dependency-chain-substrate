namespace DCS.Cli;

internal sealed record BuildVerificationResult(
    bool Succeeded,
    int ExitCode,
    string Output);
