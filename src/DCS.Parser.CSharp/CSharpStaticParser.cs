using DCS.Core.IR;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DCS.Parser.CSharp;

public sealed class CSharpStaticParser
{
    private readonly string? _repoPath;

    public CSharpStaticParser(string? repoPath = null)
    {
        _repoPath = repoPath;
    }

    /// <summary>
    /// Extract registrations from a specific git commit (git blob reading, no checkout).
    /// </summary>
    public RegistrationGraph ParseCommit(string repoPath, string commitSha)
    {
        using var repo = new Repository(repoPath);
        var commit = repo.Lookup<Commit>(commitSha)
            ?? throw new ArgumentException($"Commit {commitSha} not found in {repoPath}");

        var sourceFiles = new List<(string path, string content)>();
        CollectCSharpFiles(commit.Tree, "", sourceFiles);
        return BuildGraph(sourceFiles, commitSha);
    }

    /// <summary>
    /// Extract registrations from a directory on disk (working directory / current state).
    /// </summary>
    public RegistrationGraph ParseDirectory(string directoryPath)
    {
        var sourceFiles = Directory
            .EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcludedPath(f))
            .Select(f => (path: Path.GetRelativePath(directoryPath, f), content: File.ReadAllText(f)))
            .ToList();

        return BuildGraph(sourceFiles, commitSha: null);
    }

    private static void CollectCSharpFiles(Tree tree, string prefix, List<(string, string)> files)
    {
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            switch (entry.TargetType)
            {
                case TreeEntryTargetType.Tree:
                    CollectCSharpFiles((Tree)entry.Target, entryPath, files);
                    break;
                case TreeEntryTargetType.Blob when entry.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase):
                    var blob = (Blob)entry.Target;
                    files.Add((entryPath, blob.GetContentText()));
                    break;
            }
        }
    }

    private static RegistrationGraph BuildGraph(
        IEnumerable<(string path, string content)> sourceFiles,
        string? commitSha)
    {
        var registrations = new List<RegistrationNode>();
        var blindSpots = new List<BlindSpotReport>();
        var constructorDeps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var fileCount = 0;

        foreach (var (filePath, content) in sourceFiles)
        {
            fileCount++;
            SyntaxTree tree;
            try
            {
                tree = CSharpSyntaxTree.ParseText(content, path: filePath);
            }
            catch (Exception ex)
            {
                blindSpots.Add(new BlindSpotReport
                {
                    Pattern = "syntax_error",
                    Location = new SourceRef { FilePath = filePath },
                    Description = ex.Message
                });
                continue;
            }

            var root = tree.GetCompilationUnitRoot();
            var usings = root.Usings
                .Select(u => u.Name?.ToString() ?? string.Empty)
                .Where(u => u.Length > 0)
                .ToList();

            var regVisitor = new RegistrationPatternVisitor(filePath, usings);
            regVisitor.Visit(root);
            registrations.AddRange(regVisitor.Registrations);
            blindSpots.AddRange(regVisitor.BlindSpots);

            var ctorVisitor = new ConstructorDepVisitor();
            ctorVisitor.Visit(root);
            foreach (var (className, deps) in ctorVisitor.ConstructorDeps)
                constructorDeps[className] = deps;
        }

        var edges = BuildEdges(registrations, constructorDeps);

        return new RegistrationGraph
        {
            CommitSha = commitSha,
            Nodes = registrations,
            Edges = edges,
            BlindSpots = blindSpots,
            Metadata = new Dictionary<string, string>
            {
                ["source_file_count"] = fileCount.ToString(),
                ["registration_count"] = registrations.Count.ToString(),
                ["blind_spot_count"] = blindSpots.Count.ToString()
            }
        };
    }

    private static List<DependencyEdge> BuildEdges(
        List<RegistrationNode> nodes,
        Dictionary<string, List<string>> constructorDeps)
    {
        // Index by abstract token short name for fast lookup
        var byAbstractName = nodes
            .GroupBy(n => n.AbstractToken.ShortName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var edges = new List<DependencyEdge>();

        foreach (var node in nodes.Where(n => n.ConcreteImpl != null))
        {
            var implShortName = node.ConcreteImpl!.ShortName;
            if (!constructorDeps.TryGetValue(implShortName, out var deps))
                continue;

            var edgeIndex = 0;
            foreach (var depType in deps)
            {
                var depShortName = depType.Contains('<') ? depType[..depType.IndexOf('<')] : depType;
                if (!byAbstractName.TryGetValue(depShortName, out var depNode))
                    continue;

                edges.Add(new DependencyEdge
                {
                    Id = DependencyEdge.ComputeId(node.Id, depNode.Id, edgeIndex++),
                    From = node.Id,
                    To = depNode.Id,
                    InjectionMechanism = Mechanism.Constructor,
                    ParameterName = depType,
                    ParserConfidence =
                        node.ParserConfidence == Confidence.Explicit &&
                        depNode.ParserConfidence == Confidence.Explicit
                            ? Confidence.Explicit
                            : Confidence.Inferred
                });
            }
        }

        return edges;
    }

    private static bool IsExcludedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
}
