using LibGit2Sharp;

namespace DCS.Cli;

internal static class CommitFileReader
{
    internal static Func<string, string> Create(string repoPath, string commitSha)
    {
        var repo = new Repository(repoPath);
        var commit = repo.Lookup<Commit>(commitSha)
            ?? throw new InvalidOperationException($"Commit not found: {commitSha}");

        var files = new Dictionary<string, Blob>(StringComparer.OrdinalIgnoreCase);
        IndexTree(commit.Tree, string.Empty, files);

        return relativePath =>
        {
            var normalized = relativePath.Replace('\\', '/');
            if (!files.TryGetValue(normalized, out var blob))
            {
                throw new FileNotFoundException(
                    $"File not found at commit {commitSha[..Math.Min(8, commitSha.Length)]}: {normalized}");
            }

            return blob.GetContentText();
        };
    }

    private static void IndexTree(Tree tree, string prefix, Dictionary<string, Blob> files)
    {
        foreach (var entry in tree)
        {
            var path = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";
            switch (entry.TargetType)
            {
                case TreeEntryTargetType.Blob when entry.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase):
                    files[path] = (Blob)entry.Target;
                    break;
                case TreeEntryTargetType.Tree:
                    IndexTree((Tree)entry.Target, path, files);
                    break;
            }
        }
    }
}
