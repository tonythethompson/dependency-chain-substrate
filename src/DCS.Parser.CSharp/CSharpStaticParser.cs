using DCS.Analysis;
using DCS.Core.Caching;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Parser.CSharp.Semantic;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DCS.Parser.CSharp;

public sealed class CSharpStaticParser : IStaticParser
{
    public const string ParserVersion = "0.2.0";

    private readonly CSharpParseOptions _options;

    public CSharpStaticParser(CSharpParseOptions? options = null)
    {
        _options = options ?? new CSharpParseOptions();
    }

    public ParseResult ParseCommit(string repoPath, string commitSha)
    {
        var semanticOptions = CreateSemanticOptions();
        var cacheFingerprint = ComputeExtractionFingerprint(semanticOptions);

        var cacheDir = _options.NoCache
            ? null
            : ExtractionCache.ResolveCacheDirectory(_options.CacheDirectory);

        if (cacheDir != null)
        {
            var cached = ExtractionCache.TryReadResult(commitSha, ParserVersion, cacheDir, cacheFingerprint);
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
        CollectCSharpFiles(commit.Tree, "", sourceFiles);
        var result = BuildParseResult(sourceFiles, repoPath, commitSha, semanticOptions);

        if (cacheDir != null)
            ExtractionCache.Write(result, cacheDir, cacheFingerprint);

        return result;
    }

    public ParseResult ParseDirectory(string directoryPath)
    {
        var semanticOptions = CreateSemanticOptions();
        var sourceFiles = Directory
            .EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcludedPath(f))
            .Select(f => (path: Path.GetRelativePath(directoryPath, f), content: File.ReadAllText(f)))
            .ToList();

        return BuildParseResult(sourceFiles, directoryPath, commitSha: null, semanticOptions);
    }

    private SemanticExtractionOptions CreateSemanticOptions() => new()
    {
        BuildConfiguration = _options.BuildConfiguration,
        TargetFramework = _options.TargetFramework,
        AllTargetFrameworks = _options.AllTargetFrameworks
    };

    private static string ComputeExtractionFingerprint(SemanticExtractionOptions options)
    {
        var key = $"{options.BuildConfiguration}|{options.TargetFramework ?? "*"}|{options.AllTargetFrameworks}";
        return RegistrationNode.ComputeDuplicateGroupKey("extraction", ServiceTypeIdentity.FromSyntactic(key));
    }

    private ParseResult BuildParseResult(
        IReadOnlyList<(string path, string content)> sourceFiles,
        string repoRoot,
        string? commitSha,
        SemanticExtractionOptions semanticOptions)
    {
        var scopes = ProjectTargetScopeDiscovery.DiscoverScopes(sourceFiles, repoRoot, semanticOptions);

        if (!semanticOptions.AllTargetFrameworks && !string.IsNullOrWhiteSpace(semanticOptions.TargetFramework))
            scopes = scopes.Where(s => s.TargetFramework == semanticOptions.TargetFramework).ToList();

        var tfmGroups = semanticOptions.AllTargetFrameworks
            ? scopes.GroupBy(s => s.TargetFramework, StringComparer.OrdinalIgnoreCase).ToList()
            : [scopes.GroupBy(s => s.TargetFramework).First()];

        var contextGraphs = new List<ContextGraph>();
        foreach (var tfmGroup in tfmGroups)
        {
            var scopeSubset = tfmGroup.ToList();
            var graph = BuildGraphForScopes(sourceFiles, scopeSubset, commitSha);
            graph.Metadata["target_framework"] = tfmGroup.Key;
            contextGraphs.Add(new ContextGraph
            {
                ContextId = $"csharp|{tfmGroup.Key}",
                EntryRoot = TypeRef.FromQualifiedName(tfmGroup.Key),
                Graph = graph
            });
        }

        if (contextGraphs.Count == 0)
        {
            var empty = BuildGraphForScopes(sourceFiles, scopes.ToList(), commitSha);
            return CSharpParseResultFactory.Wrap(empty);
        }

        return new ParseResult { ContextGraphs = contextGraphs };
    }

    private RegistrationGraph BuildGraphForScopes(
        IReadOnlyList<(string path, string content)> sourceFiles,
        IReadOnlyList<ProjectTargetScope> scopes,
        string? commitSha)
    {
        var orderedScopes = ProjectReferenceClosureOrder.SortTopologically(scopes);
        var fileContents = sourceFiles.ToDictionary(
            f => f.path.Replace('\\', '/'),
            f => f.content,
            StringComparer.OrdinalIgnoreCase);

        var scopeCompilations = new Dictionary<string, ScopeCompilationResult>(StringComparer.Ordinal);
        var projectAssemblyRefs = new Dictionary<string, MetadataReference>(StringComparer.Ordinal);

        foreach (var scope in orderedScopes)
        {
            var additionalRefs = scope.ProjectReferences
                .Select(pref =>
                {
                    var match = orderedScopes.FirstOrDefault(s =>
                        s.CsprojPath.Equals(pref, StringComparison.OrdinalIgnoreCase));
                    return match != null && projectAssemblyRefs.TryGetValue(match.ScopeId, out var r) ? r : null;
                })
                .Where(r => r != null)
                .Cast<MetadataReference>()
                .ToList();

            var compilation = ProjectTargetScopeCompilationFactory.Build(scope, fileContents, additionalRefs);
            scopeCompilations[scope.ScopeId] = compilation;

            try
            {
                projectAssemblyRefs[scope.ScopeId] = compilation.Compilation.ToMetadataReference();
            }
            catch { /* skip invalid compilation refs */ }
        }

        var registrations = new List<RegistrationNode>();
        var blindSpots = new List<BlindSpotReport>();
        var constructorDeps = new Dictionary<string, ConstructorDependency>(StringComparer.Ordinal);
        var profileFingerprints = new List<string>();
        var fileCount = 0;

        foreach (var scope in orderedScopes)
        {
            if (!scopeCompilations.TryGetValue(scope.ScopeId, out var scopeResult))
                continue;

            profileFingerprints.Add(scopeResult.ReferenceProfile.Fingerprint);

            foreach (var relativePath in scope.SourceFiles)
            {
                var normPath = relativePath.Replace('\\', '/');
                if (!scopeResult.ModelsByPath.TryGetValue(normPath, out var model))
                    continue;

                fileCount++;
                var root = model.SyntaxTree.GetCompilationUnitRoot();
                var usings = root.Usings
                    .Select(u => u.Name?.ToString() ?? string.Empty)
                    .Where(u => u.Length > 0)
                    .ToList();

                var semanticCtx = new SemanticRegistrationContext { Scope = scope, Model = model };

                var regVisitor = new RegistrationPatternVisitor(normPath, usings, _options.Boundaries, semanticCtx);
                regVisitor.Visit(root);
                registrations.AddRange(regVisitor.Registrations);
                blindSpots.AddRange(regVisitor.BlindSpots);

                var ctorVisitor = new ConstructorDepVisitor(model, scope.ScopeId);
                ctorVisitor.Visit(root);
                foreach (var (key, dep) in ctorVisitor.ConstructorDeps)
                    constructorDeps[key] = dep;
            }
        }

        var (edges, unresolved) = BuildEdges(registrations, constructorDeps);

        return new RegistrationGraph
        {
            ParserVersion = ParserVersion,
            CommitSha = commitSha,
            Nodes = registrations,
            Edges = edges,
            BlindSpots = blindSpots,
            UnresolvedInjections = unresolved,
            Metadata = new Dictionary<string, string>
            {
                ["source_file_count"] = fileCount.ToString(),
                ["registration_count"] = registrations.Count.ToString(),
                ["blind_spot_count"] = blindSpots.Count.ToString(),
                ["reference_profile_fingerprint"] = string.Join(";", profileFingerprints.Distinct()),
                ["semantic_parser"] = "true"
            }
        };
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
                    files.Add((entryPath, ((Blob)entry.Target).GetContentText()));
                    break;
            }
        }
    }

    private static (List<DependencyEdge>, List<UnresolvedInjection>) BuildEdges(
        List<RegistrationNode> nodes,
        Dictionary<string, ConstructorDependency> constructorDeps)
    {
        var byServiceIdentity = nodes
            .Where(n => n.ServiceType?.IsResolved == true)
            .GroupBy(n => n.ServiceType!.CanonicalKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var edges = new List<DependencyEdge>();
        var unresolved = new List<UnresolvedInjection>();
        var edgeIndexGlobal = 0;

        foreach (var node in nodes.Where(n => n.ConcreteImpl != null))
        {
            var implShort = node.ConcreteImpl!.ShortName;
            var depEntry = constructorDeps.Values.FirstOrDefault(d =>
                d.ImplementationShortName.Equals(implShort, StringComparison.Ordinal));

            if (depEntry == null)
                continue;

            var edgeIndex = 0;
            foreach (var param in depEntry.Parameters)
            {
                if (param.Quality != TypeResolutionQuality.Resolved || param.Identity == null)
                {
                    unresolved.Add(new UnresolvedInjection
                    {
                        Id = UnresolvedInjection.ComputeId(node.Id, param.SyntacticName, edgeIndexGlobal++),
                        FromRegistrationId = node.Id,
                        DeclaredType = TypeIdentityFormatter.SyntacticFallbackTypeRef(param.SyntacticName),
                        ParameterName = param.SyntacticName,
                        Reason = "semantic_unresolved"
                    });
                    continue;
                }

                var serviceKey = ServiceTypeIdentity.FromResolved(param.Identity).CanonicalKey;
                if (!byServiceIdentity.TryGetValue(serviceKey, out var candidates))
                {
                    unresolved.Add(new UnresolvedInjection
                    {
                        Id = UnresolvedInjection.ComputeId(node.Id, serviceKey, edgeIndexGlobal++),
                        FromRegistrationId = node.Id,
                        DeclaredType = TypeIdentityFormatter.ToTypeRef(param.Identity),
                        ParameterName = param.SyntacticName,
                        Reason = "no_matching_registration"
                    });
                    continue;
                }

                var depNode = candidates.FirstOrDefault(c =>
                    c.CompositionScopeId == node.CompositionScopeId) ?? candidates.First();

                edges.Add(new DependencyEdge
                {
                    Id = DependencyEdge.ComputeId(node.Id, depNode.Id, edgeIndex++),
                    From = node.Id,
                    To = depNode.Id,
                    InjectionMechanism = Mechanism.Constructor,
                    ParameterName = param.SyntacticName,
                    ParserConfidence =
                        node.ParserConfidence == Confidence.Explicit &&
                        depNode.ParserConfidence == Confidence.Explicit
                            ? Confidence.Explicit
                            : Confidence.Inferred
                });
            }
        }

        return (edges, unresolved);
    }

    private static bool IsExcludedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
}
