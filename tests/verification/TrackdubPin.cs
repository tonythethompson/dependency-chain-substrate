namespace DCS.Verification;

/// <summary>
/// Pinned Trackdub revision for semantic parser CI verification.
/// </summary>
public static class TrackdubPin
{
    public const string CommitSha = "3c4e374d23fe3941ed7ca376775937941973b313";
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
