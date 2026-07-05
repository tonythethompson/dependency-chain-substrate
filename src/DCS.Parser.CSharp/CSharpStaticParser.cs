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
    public const string ParserVersion = "0.3.5";

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
        var commitCsprojContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectCSharpFiles(commit.Tree, "", sourceFiles, _options.IncludeTests);
        CollectCsprojFiles(commit.Tree, "", commitCsprojContents, _options.IncludeTests);
        var result = BuildParseResult(sourceFiles, repoPath, commitSha, semanticOptions, commitCsprojContents);

        if (cacheDir != null)
            ExtractionCache.Write(result, cacheDir, cacheFingerprint);

        return result;
    }

    public ParseResult ParseDirectory(string directoryPath)
    {
        var semanticOptions = CreateSemanticOptions();
        var sourceFiles = Directory
            .EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcludedPath(f, _options.IncludeTests))
            .Select(f => (path: Path.GetRelativePath(directoryPath, f), content: File.ReadAllText(f)))
            .ToList();

        return BuildParseResult(sourceFiles, directoryPath, commitSha: null, semanticOptions, commitCsprojContents: null);
    }

    private SemanticExtractionOptions CreateSemanticOptions() => new()
    {
        BuildConfiguration = _options.BuildConfiguration,
        TargetFramework = _options.TargetFramework,
        AllTargetFrameworks = _options.AllTargetFrameworks,
        IncludeTests = _options.IncludeTests
    };

    private static string ComputeExtractionFingerprint(SemanticExtractionOptions options)
    {
        var key = $"{options.BuildConfiguration}|{options.TargetFramework ?? "*"}|{options.AllTargetFrameworks}|tests={options.IncludeTests}";
        return RegistrationNode.ComputeDuplicateGroupKey("extraction", ServiceTypeIdentity.FromSyntactic(key));
    }

    private ParseResult BuildParseResult(
        IReadOnlyList<(string path, string content)> sourceFiles,
        string repoRoot,
        string? commitSha,
        SemanticExtractionOptions semanticOptions,
        IReadOnlyDictionary<string, string>? commitCsprojContents = null)
    {
        var allDiscoveredScopes = ProjectTargetScopeDiscovery.DiscoverScopes(
            sourceFiles, repoRoot, semanticOptions, commitCsprojContents);

        var scopes = allDiscoveredScopes;
        if (!semanticOptions.AllTargetFrameworks)
        {
            var tfm = semanticOptions.TargetFramework;
            if (string.IsNullOrWhiteSpace(tfm))
            {
                tfm = TargetFrameworkSelector.SelectPrimaryTargetFramework(
                    scopes.Select(s => s.TargetFramework));
            }

            scopes = scopes
                .Where(s => string.Equals(s.TargetFramework, tfm, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var tfmGroups = scopes
            .GroupBy(s => s.TargetFramework, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var contextGraphs = new List<ContextGraph>();
        foreach (var tfmGroup in tfmGroups)
        {
            var scopeSubset = tfmGroup.ToList();
            var graph = BuildGraphForScopes(sourceFiles, scopeSubset, commitSha, allDiscoveredScopes);
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
            var empty = BuildGraphForScopes(sourceFiles, scopes.ToList(), commitSha, allDiscoveredScopes);
            return CSharpParseResultFactory.Wrap(empty);
        }

        return new ParseResult { ContextGraphs = contextGraphs };
    }

    private RegistrationGraph BuildGraphForScopes(
        IReadOnlyList<(string path, string content)> sourceFiles,
        IReadOnlyList<ProjectTargetScope> activeScopes,
        string? commitSha,
        IReadOnlyList<ProjectTargetScope> allScopes)
    {
        var extractionScopeIds = activeScopes.Select(s => s.ScopeId).ToHashSet(StringComparer.Ordinal);
        var compilationScopes = CrossTfmProjectReferenceResolver.ExpandWithReferencedScopes(activeScopes, allScopes);
        var orderedScopes = ProjectReferenceClosureOrder.SortTopologically(compilationScopes);
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
                    var match = CrossTfmProjectReferenceResolver.FindScopeForProjectReference(
                        pref, scope.TargetFramework, orderedScopes);
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
            if (!extractionScopeIds.Contains(scope.ScopeId))
                continue;

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
        registrations = FactoryNodeConfidenceRefiner.Refine(registrations, edges, unresolved);

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

    private static void CollectCSharpFiles(Tree tree, string prefix, List<(string, string)> files, bool includeTests)
    {
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            switch (entry.TargetType)
            {
                case TreeEntryTargetType.Tree:
                    CollectCSharpFiles((Tree)entry.Target, entryPath, files, includeTests);
                    break;
                case TreeEntryTargetType.Blob when entry.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase):
                    if (!IsExcludedPath(entryPath, includeTests))
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

        var byCanonicalKey = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.ServiceType?.CanonicalKey))
            .GroupBy(n => n.ServiceType!.CanonicalKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var depsByShortName = constructorDeps.Values
            .GroupBy(d => d.ImplementationShortName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var edges = new List<DependencyEdge>();
        var unresolved = new List<UnresolvedInjection>();
        var edgeIndexGlobal = 0;

        foreach (var node in nodes.Where(n => n.ConcreteImpl != null))
        {
            var concrete = node.ConcreteImpl!;
            var implShort = concrete.ShortName;

            ConstructorDependency? depEntry = null;
            if (depsByShortName.TryGetValue(implShort, out var byShort))
            {
                depEntry = byShort.Count == 1
                    ? byShort[0]
                    : byShort.FirstOrDefault(d =>
                        d.ImplementationIdentity != null &&
                        MatchesConcreteImplementation(d.ImplementationIdentity, concrete));
                depEntry ??= byShort[0];
            }

            if (depEntry == null)
                continue;

            var edgeIndex = 0;
            foreach (var param in depEntry.Parameters)
                TryAddDependencyEdge(node, param, byServiceIdentity, nodes, edges, unresolved, ref edgeIndex, ref edgeIndexGlobal);
        }

        foreach (var node in nodes)
        {
            if (!node.Annotations.TryGetValue("factory_lambda_service_keys", out var keysRaw))
                continue;

            var factoryEdgeIndex = 0;
            foreach (var serviceKey in keysRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsFrameworkServiceKey(serviceKey))
                    continue;

                if (!byCanonicalKey.TryGetValue(serviceKey, out var candidates))
                    candidates = TryResolveOptionsCandidatesFromKey(serviceKey, nodes);

                if (candidates == null || candidates.Count == 0)
                {
                    unresolved.Add(new UnresolvedInjection
                    {
                        Id = UnresolvedInjection.ComputeId(node.Id, serviceKey, edgeIndexGlobal++),
                        FromRegistrationId = node.Id,
                        DeclaredType = TypeRef.FromShortName(serviceKey),
                        ParameterName = serviceKey,
                        InjectionMechanism = Mechanism.FactoryParameter,
                        Reason = "no_matching_registration"
                    });
                    continue;
                }

                var depNode = candidates.FirstOrDefault(c =>
                    c.CompositionScopeId == node.CompositionScopeId) ?? candidates.First();

                edges.Add(new DependencyEdge
                {
                    Id = DependencyEdge.ComputeId(node.Id, depNode.Id, factoryEdgeIndex++),
                    From = node.Id,
                    To = depNode.Id,
                    InjectionMechanism = Mechanism.FactoryParameter,
                    ParameterName = depNode.DisplayName,
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

    private static bool TryAddDependencyEdge(
        RegistrationNode node,
        ResolvedParameterDependency param,
        Dictionary<string, List<RegistrationNode>> byServiceIdentity,
        List<RegistrationNode> nodes,
        List<DependencyEdge> edges,
        List<UnresolvedInjection> unresolved,
        ref int edgeIndex,
        ref int edgeIndexGlobal)
    {
        if (FrameworkProvidedServices.IsFrameworkProvided(
                param.SyntacticName,
                param.Identity?.MetadataName))
            return true;

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
            return true;
        }

        var serviceKey = ServiceTypeIdentity.FromResolved(param.Identity).CanonicalKey;
        if (!byServiceIdentity.TryGetValue(serviceKey, out var candidates))
        {
            candidates = TryResolveOptionsCandidates(param, nodes) ??
                         TryResolveConfiguredOptionsByGenericArg(param, nodes);
            if (candidates == null)
            {
                unresolved.Add(new UnresolvedInjection
                {
                    Id = UnresolvedInjection.ComputeId(node.Id, serviceKey, edgeIndexGlobal++),
                    FromRegistrationId = node.Id,
                    DeclaredType = TypeIdentityFormatter.ToTypeRef(param.Identity),
                    ParameterName = param.SyntacticName,
                    Reason = "no_matching_registration"
                });
                return true;
            }
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
        return true;
    }

    private static List<RegistrationNode>? TryResolveOptionsCandidates(
        ResolvedParameterDependency param,
        List<RegistrationNode> nodes)
    {
        var metadataName = param.Identity?.MetadataName;
        if (metadataName == null || !metadataName.StartsWith("Microsoft.Extensions.Options.IOptions", StringComparison.Ordinal))
            return null;

        var optionsTypeName = param.Identity?.TypeArguments.FirstOrDefault()?.MetadataName;
        if (string.IsNullOrEmpty(optionsTypeName))
            return null;

        var shortName = optionsTypeName.Split('.').Last();
        var matches = nodes
            .Where(n => n.Annotations.GetValueOrDefault("pattern") == "options_configuration" &&
                        string.Equals(n.Annotations.GetValueOrDefault("options_type"), shortName, StringComparison.Ordinal))
            .ToList();
        return matches.Count > 0 ? matches : null;
    }

    private static List<RegistrationNode>? TryResolveConfiguredOptionsByGenericArg(
        ResolvedParameterDependency param,
        List<RegistrationNode> nodes)
    {
        var shortName = param.Identity?.MetadataName?.Split('.').Last();
        if (string.IsNullOrEmpty(shortName))
            return null;

        var matches = nodes
            .Where(n => n.Annotations.GetValueOrDefault("pattern") == "options_configuration" &&
                        (string.Equals(n.DisplayName, shortName, StringComparison.Ordinal) ||
                         string.Equals(n.Annotations.GetValueOrDefault("options_type"), shortName, StringComparison.Ordinal)))
            .ToList();
        return matches.Count > 0 ? matches : null;
    }

    private static List<RegistrationNode>? TryResolveOptionsCandidatesFromKey(
        string serviceKey,
        List<RegistrationNode> nodes)
    {
        if (!serviceKey.Contains("IOptions", StringComparison.Ordinal))
            return null;

        var matches = nodes
            .Where(n => n.Annotations.GetValueOrDefault("pattern") == "options_configuration")
            .ToList();
        return matches.Count > 0 ? matches : null;
    }

    private static bool MatchesConcreteImplementation(ResolvedTypeIdentity identity, TypeRef concrete)
    {
        if (string.Equals(identity.MetadataName, concrete.FullyQualifiedName, StringComparison.Ordinal))
            return true;

        if (string.Equals(identity.MetadataName, concrete.ShortName, StringComparison.Ordinal))
            return true;

        return identity.MetadataName.EndsWith(
            $".{concrete.ShortName}",
            StringComparison.Ordinal);
    }

    private static void CollectCsprojFiles(
        Tree tree,
        string prefix,
        Dictionary<string, string> files,
        bool includeTests)
    {
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            switch (entry.TargetType)
            {
                case TreeEntryTargetType.Tree:
                    CollectCsprojFiles((Tree)entry.Target, entryPath, files, includeTests);
                    break;
                case TreeEntryTargetType.Blob when entry.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase):
                    if (includeTests || !ShellCompositionScope.IsTestCsprojPath(entryPath))
                        files[entryPath.Replace('\\', '/')] = ((Blob)entry.Target).GetContentText();
                    break;
            }
        }
    }

    private static bool IsFrameworkServiceKey(string serviceKey)
    {
        if (serviceKey.StartsWith("Microsoft.Extensions.Http|", StringComparison.Ordinal))
            return true;

        string? syntactic = serviceKey.StartsWith("syntactic:", StringComparison.Ordinal)
            ? serviceKey["syntactic:".Length..]
            : null;

        string? fullyQualified = null;
        if (serviceKey.StartsWith("scope:", StringComparison.Ordinal))
        {
            var parts = serviceKey.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
                fullyQualified = parts[^1];
        }

        if (syntactic == null && fullyQualified != null)
        {
            var lastDot = fullyQualified.LastIndexOf('.');
            syntactic = lastDot >= 0 ? fullyQualified[(lastDot + 1)..] : fullyQualified;
        }

        return syntactic != null &&
               FrameworkProvidedServices.IsFrameworkProvided(syntactic, fullyQualified);
    }

    private static bool IsExcludedPath(string path, bool includeTests)
    {
        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
            path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") ||
            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            return true;

        var normalized = path.Replace('\\', '/');
        if (IsAgentArtifactPath(normalized))
            return true;

        if (includeTests)
            return false;

        if (normalized.Contains("/tests/fixtures/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tests/fixtures/", StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAgentArtifactPath(string normalized)
    {
        return normalized.Contains("/.claude/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".claude/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/.cursor/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".cursor/", StringComparison.OrdinalIgnoreCase);
    }
}
