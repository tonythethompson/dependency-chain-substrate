namespace DCS.Cli;

internal enum RepoLanguage
{
    Auto,
    CSharp,
    Java
}

internal static class RepoLanguageDetector
{
    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "target", "build", ".git", "node_modules", ".gradle"
    };

    public static RepoLanguage Resolve(string? repoPath, RepoLanguage requested)
    {
        if (requested != RepoLanguage.Auto)
            return requested;

        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return RepoLanguage.CSharp;

        if (HasJavaBuildMarker(repoPath))
            return RepoLanguage.Java;

        if (HasCSharpProject(repoPath))
            return RepoLanguage.CSharp;

        var (javaCount, csharpCount) = CountSourceFiles(repoPath);
        if (javaCount > csharpCount)
            return RepoLanguage.Java;
        if (csharpCount > javaCount)
            return RepoLanguage.CSharp;

        return RepoLanguage.CSharp;
    }

    private static bool HasJavaBuildMarker(string repoPath)
    {
        if (File.Exists(Path.Combine(repoPath, "pom.xml")))
            return true;

        foreach (var name in new[] { "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts" })
        {
            if (File.Exists(Path.Combine(repoPath, name)))
                return true;
        }

        return false;
    }

    private static bool HasCSharpProject(string repoPath) =>
        Directory.EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
            .Any(path => !IsUnderExcludedDirectory(path));

    private static (int Java, int CSharp) CountSourceFiles(string repoPath)
    {
        var java = 0;
        var csharp = 0;

        foreach (var file in Directory.EnumerateFiles(repoPath, "*.*", SearchOption.AllDirectories))
        {
            if (IsUnderExcludedDirectory(file))
                continue;

            if (file.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
                java++;
            else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                csharp++;
        }

        return (java, csharp);
    }

    private static bool IsUnderExcludedDirectory(string filePath)
    {
        foreach (var segment in filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ExcludedDirNames.Contains(segment))
                return true;
        }

        return false;
    }

    public static RepoLanguage ParseLanguageFlag(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "auto" => RepoLanguage.Auto,
            "csharp" or "cs" or "c#" => RepoLanguage.CSharp,
            "java" or "spring" => RepoLanguage.Java,
            _ => throw new InvalidOperationException(
                $"Unknown language \"{value}\". Use auto, csharp, or java.")
        };
}
