using DCS.Core.Caching;
using DCS.Core.Parsing;
using LibGit2Sharp;

namespace DCS.Parser.Java;

/// <summary>
/// Static Spring/Java DI extraction with context-aware conservative graph assembly.
/// </summary>
public sealed class SpringStaticParser : IStaticParser
{
    public const string ParserVersion = "0.1.0";

    private readonly JavaParseOptions _options;

    public SpringStaticParser(JavaParseOptions? options = null)
    {
        _options = options ?? new JavaParseOptions();
    }

    public ParseResult ParseCommit(string repoPath, string commitSha)
    {
        var cacheDir = _options.NoCache
            ? null
            : ExtractionCache.ResolveCacheDirectory(_options.CacheDirectory);

        if (cacheDir != null)
        {
            var cached = ExtractionCache.TryReadResult(commitSha, ParserVersion, cacheDir);
            if (cached != null)
            {
                _options.OnCacheHit?.Invoke(commitSha);
                return cached;
            }
        }

        using var repo = new Repository(repoPath);
        var commit = repo.Lookup<Commit>(commitSha)
            ?? throw new ArgumentException($"Commit {commitSha} not found in {repoPath}");

        var sourceFiles = new List<(string path, string content)>();
        CollectJavaFiles(commit.Tree, "", sourceFiles);
        var result = new SpringGraphBuilder(_options).Build(sourceFiles, repoPath, commitSha);

        if (cacheDir != null)
            ExtractionCache.Write(result, cacheDir);

        return result;
    }

    public ParseResult ParseDirectory(string directoryPath)
    {
        var sourceFiles = Directory
            .EnumerateFiles(directoryPath, "*.java", SearchOption.AllDirectories)
            .Where(f => !IsExcludedPath(f))
            .Select(f => (path: Path.GetRelativePath(directoryPath, f), content: File.ReadAllText(f)))
            .ToList();

        return new SpringGraphBuilder(_options).Build(sourceFiles, directoryPath, commitSha: null);
    }

    private static void CollectJavaFiles(Tree tree, string prefix, List<(string, string)> files)
    {
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            switch (entry.TargetType)
            {
                case TreeEntryTargetType.Tree:
                    CollectJavaFiles((Tree)entry.Target, entryPath, files);
                    break;
                case TreeEntryTargetType.Blob when entry.Name.EndsWith(".java", StringComparison.OrdinalIgnoreCase):
                    var blob = (Blob)entry.Target;
                    files.Add((entryPath, blob.GetContentText()));
                    break;
            }
        }
    }

    private static bool IsExcludedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}target{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}");
}
