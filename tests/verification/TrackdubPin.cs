namespace DCS.Verification;

/// <summary>
/// Pinned Trackdub revision for semantic parser CI verification.
/// </summary>
public static class TrackdubPin
{
    /// <summary>
    /// Avalonia-only shell; WinUI Trackdub.App retired. GitHub main @ b57fc832 (2026-07-05).
    /// </summary>
    public const string CommitSha = "b57fc8327e4773fb686cc77025d2b57bbb37cb85";
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
