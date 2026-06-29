namespace DCS.Verification;

/// <summary>
/// Pinned Trackdub revision for semantic parser CI verification.
/// </summary>
public static class TrackdubPin
{
    public const string CommitSha = "3c4e374d";
    public const string RepositoryUrl = "https://github.com/tonythethompson/Trackdub.git";

    /// <summary>
    /// Default local clone path (override with TRACKDUB_PATH).
    /// </summary>
    public const string DefaultLocalPath = @"A:\Trackdub";

    public static string? ResolvePath()
    {
        var env = Environment.GetEnvironmentVariable("TRACKDUB_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        if (Directory.Exists(DefaultLocalPath))
            return DefaultLocalPath;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI")))
        {
            var cloneDir = Path.Combine(Path.GetTempPath(), "dcs-trackdub-pin");
            if (Directory.Exists(Path.Combine(cloneDir, ".git")))
                return cloneDir;

            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                throw new InvalidOperationException(
                    $"Trackdub not found at {cloneDir}. CI must clone Trackdub at {CommitSha}.");
            }
        }

        return null;
    }
}
