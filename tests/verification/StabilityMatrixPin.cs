namespace DCS.Verification;

/// <summary>
/// Pinned StabilityMatrix revision for C# negative-control verification
/// (single Avalonia shell, no LEAKED/DUPLICATE on primary desktop project).
/// </summary>
public static class StabilityMatrixPin
{
    public const string CommitSha = "d97f6ccb9634a7ccfa7513be083aa70653112147";
    public const string RepositoryUrl = "https://github.com/LykosAI/StabilityMatrix.git";
    public const string CorpusId = CorpusGateTraits.CsharpNegativeControl;
    public const string CheckoutPath = "corpus/csharp-negative-control";

    /// <summary>
    /// Subdirectory within the repo root to analyze (legacy WinUI/Avalonia desktop app;
    /// excludes StabilityMatrix.Avalonia where duplicate registrations appear).
    /// </summary>
    public const string AnalyzeSubdirectory = "StabilityMatrix";
}
