namespace DCS.Verification;

/// <summary>
/// Pinned Trackdub revision for semantic parser CI verification.
/// </summary>
public static class TrackdubPin
{
    /// <summary>
    /// Avalonia-only shell; WinUI Trackdub.App retired. Supersedes mid-migration pin 3c4e374d (2026-07-05).
    /// </summary>
    public const string CommitSha = "5fd8b4814c9142f3980999c178b49adae9e725a6";
    public const string RepositoryUrl = "https://github.com/tonythethompson/Trackdub.git";
    public const string CorpusId = CorpusGateTraits.CsharpMigration;
    public const string CheckoutPath = "corpus/csharp-migration";

    /// <summary>
    /// Default local clone path (override with CORPUS_CSHARP_MIGRATION_PATH or TRACKDUB_PATH).
    /// </summary>
    public const string DefaultLocalPath = @"A:\Trackdub";

    public static string? ResolvePath() =>
        CorpusPathResolver.ResolveWithDefaults(
            primaryEnvVar: "CORPUS_CSHARP_MIGRATION_PATH",
            legacyEnvVar: "TRACKDUB_PATH",
            defaultLocalPath: DefaultLocalPath,
            tempCloneDirName: "corpus-csharp-migration",
            workspaceRelativeCheckoutPath: CheckoutPath);
}
