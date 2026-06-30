namespace DCS.Verification;

/// <summary>
/// Pinned Trackdub revision for semantic parser CI verification.
/// </summary>
public static class TrackdubPin
{
    public const string CommitSha = "3c4e374d23fe3941ed7ca376775937941973b313";
    public const string RepositoryUrl = "https://github.com/tonythethompson/Trackdub.git";

    /// <summary>
    /// Default local clone path (override with TRACKDUB_PATH).
    /// </summary>
    public const string DefaultLocalPath = @"A:\Trackdub";

    public static string? ResolvePath()
    {
        var env = Environment.GetEnvironmentVariable("TRACKDUB_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (Directory.Exists(env))
                return env;

            throw new InvalidOperationException(
                $"TRACKDUB_PATH is set to '{env}' but the directory does not exist.");
        }

        if (Directory.Exists(DefaultLocalPath))
            return DefaultLocalPath;

        var cloneDir = Path.Combine(Path.GetTempPath(), "dcs-trackdub-pin");
        if (Directory.Exists(Path.Combine(cloneDir, ".git")))
            return cloneDir;

        return null;
    }
}
