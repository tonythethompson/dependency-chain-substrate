namespace DCS.Verification;

/// <summary>
/// Resolves on-disk corpus paths from CI env vars (see ci/corpus-gates.json pathEnv)
/// with legacy fallbacks for local development.
/// </summary>
public static class CorpusPathResolver
{
    public static string? Resolve(params string[] envVarNames)
    {
        foreach (var name in envVarNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (Directory.Exists(value))
                return value;

            throw new InvalidOperationException(
                $"{name} is set to '{value}' but the directory does not exist.");
        }

        return null;
    }

    public static string? ResolveWithDefaults(
        string primaryEnvVar,
        string legacyEnvVar,
        string defaultLocalPath,
        string tempCloneDirName,
        string? workspaceRelativeCheckoutPath = null)
    {
        var dcsCorpus = Environment.GetEnvironmentVariable("DCS_CORPUS_PATH");
        if (!string.IsNullOrWhiteSpace(dcsCorpus))
        {
            if (Directory.Exists(dcsCorpus))
                return dcsCorpus;

            throw new InvalidOperationException(
                $"DCS_CORPUS_PATH is set to '{dcsCorpus}' but the directory does not exist.");
        }

        var resolved = Resolve(primaryEnvVar, legacyEnvVar);
        if (resolved != null)
            return resolved;

        if (!string.IsNullOrEmpty(defaultLocalPath) && Directory.Exists(defaultLocalPath))
            return defaultLocalPath;

        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(workspace) && workspaceRelativeCheckoutPath != null)
        {
            var workspacePath = Path.Combine(workspace, workspaceRelativeCheckoutPath);
            if (Directory.Exists(Path.Combine(workspacePath, ".git")))
                return workspacePath;
        }

        var cloneDir = Path.Combine(Path.GetTempPath(), tempCloneDirName);
        if (Directory.Exists(Path.Combine(cloneDir, ".git")))
            return cloneDir;

        if (IsCi())
        {
            var expected = workspaceRelativeCheckoutPath ?? $"temp:{tempCloneDirName}";
            throw new InvalidOperationException(
                $"Corpus path is required in CI but was not found. Set DCS_CORPUS_PATH, {primaryEnvVar}, " +
                $"{legacyEnvVar}, or ensure checkout path '{expected}' exists.");
        }

        return null;
    }

    private static bool IsCi() =>
        IsTruthy(Environment.GetEnvironmentVariable("CI")) ||
        IsTruthy(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
}
