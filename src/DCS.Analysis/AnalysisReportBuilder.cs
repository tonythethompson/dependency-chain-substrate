using DCS.Core.IR;

namespace DCS.Analysis;

public sealed class AnalysisReportBuildOptions
{
    public FindingPolicyOptions Policy { get; init; } = FindingPolicyOptions.Default;
    public ReportVerbosity Verbosity { get; init; } = ReportVerbosity.Actionable;
    public bool VerboseBlindSpots { get; init; }
    public bool IncludeMetrics { get; init; }
    public string? ContextId { get; init; }
    public string? TargetFramework { get; init; }
    public string? ParserVersion { get; init; }
    public IReadOnlyList<string> AvailableContexts { get; init; } = [];
}

public static class AnalysisReportBuilder
{
    public static AnalysisReport Build(
        RegistrationGraph graph,
        AnalysisResult result,
        AnalysisReportBuildOptions? options = null)
    {
        options ??= new AnalysisReportBuildOptions();
        var policy = options.Policy;
        var nodeById = IndexNodes(graph.Nodes);
        var findings = new List<AnalysisFinding>();

        findings.AddRange(BuildLeakedFindings(result.Leaked, nodeById));
        findings.AddRange(BuildBrokenFindings(result.BrokenChains, nodeById));
        findings.AddRange(BuildDuplicateFindings(result.Duplicates, graph, nodeById, policy, strict: true));
        findings.AddRange(BuildDuplicateFindings(result.PossibleDuplicates, graph, nodeById, policy, strict: false));
        findings.AddRange(BuildIntentionalTryAddFindings(graph, nodeById, policy));
        findings.AddRange(BuildUnresolvedFindings(graph, nodeById, policy));
        findings.AddRange(BuildOrphanedFindings(result.Orphaned, nodeById));
        findings.AddRange(BuildCycleFindings(result.Cycles, graph, nodeById));
        findings.AddRange(BuildBlindSpotFindings(graph.BlindSpots, policy));

        var filtered = FilterByVerbosity(findings, options.Verbosity, options.VerboseBlindSpots);
        var summary = BuildSummary(findings, result);

        return new AnalysisReport
        {
            CommitSha = graph.CommitSha,
            ContextId = options.ContextId,
            TargetFramework = options.TargetFramework,
            ParserVersion = options.ParserVersion,
            TotalNodes = result.TotalNodes,
            TotalEdges = result.TotalEdges,
            Metrics = options.IncludeMetrics ? ExtractionQualityMetricsComputer.Compute(graph.Nodes) : null,
            Summary = summary,
            Findings = filtered,
            AvailableContexts = options.AvailableContexts
        };
    }

    public static IReadOnlyList<AnalysisFinding> FilterByVerbosity(
        IReadOnlyList<AnalysisFinding> findings,
        ReportVerbosity verbosity,
        bool verboseBlindSpots)
    {
        return verbosity switch
        {
            ReportVerbosity.Full => findings,
            ReportVerbosity.Summary => findings.Where(f =>
                f.Severity == FindingSeverity.Error ||
                f.Tier == FindingTier.Actionable).ToList(),
            _ => findings.Where(f =>
            {
                if (f.Severity == FindingSeverity.Error)
                    return true;
                if (f.Tier == FindingTier.Actionable)
                    return true;
                if (verboseBlindSpots && f.Category == FindingCategory.BlindSpot)
                    return true;
                return false;
            }).ToList()
        };
    }

    private static AnalysisReportSummary BuildSummary(IReadOnlyList<AnalysisFinding> allFindings, AnalysisResult result)
    {
        var actionable = allFindings.Count(f => f.Tier == FindingTier.Actionable);
        var informational = allFindings.Count(f => f.Tier == FindingTier.Informational);
        var parserLimit = allFindings.Count(f => f.Tier == FindingTier.ParserLimit);
        var intentional = allFindings.Count(f => f.Tier == FindingTier.Intentional);

        var actionableUnresolved = allFindings.Count(f =>
            f.Category == FindingCategory.Unresolved && f.Tier == FindingTier.Actionable);
        var actionableBlindSpots = allFindings.Count(f =>
            f.Category == FindingCategory.BlindSpot && f.Tier == FindingTier.Actionable);

        return new AnalysisReportSummary
        {
            ActionableCount = actionable,
            InformationalCount = informational,
            ParserLimitCount = parserLimit,
            IntentionalCount = intentional,
            ErrorCount = allFindings.Count(f => f.Severity == FindingSeverity.Error),
            HasErrors = result.HasErrors,
            LeakedCount = result.Leaked.Count,
            BrokenCount = result.BrokenChains.Count,
            DuplicateCount = result.Duplicates.Count,
            PossibleDuplicateCount = result.PossibleDuplicates.Count,
            UnresolvedCount = result.TotalUnresolvedInjections,
            OrphanedCount = result.Orphaned.Count,
            CycleCount = result.Cycles.Count,
            BlindSpotCount = result.TotalBlindSpots
        };
    }

    private static IEnumerable<AnalysisFinding> BuildIntentionalTryAddFindings(
        RegistrationGraph graph,
        Dictionary<string, RegistrationNode> nodeById,
        FindingPolicyOptions policy)
    {
        if (policy.Strict)
            yield break;

        var groups = graph.Nodes
            .Where(StrictDuplicateEligibility.IsEligible)
            .GroupBy(n => n.DuplicateGroupKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1 && FindingPolicy.IsIntentionalTryAddOverride(g.ToList(), policy));

        foreach (var g in groups)
        {
            var nodes = g.ToList();
            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.Duplicate, g.Key, "intentional"),
                Category = FindingCategory.Duplicate,
                Severity = FindingSeverity.Warn,
                Tier = FindingTier.Intentional,
                Title = nodes[0].DisplayName,
                Detail = "TryAdd baseline + explicit Add override",
                Sites = nodes.Select(SiteFromNode).ToList()
            };
        }
    }

    private static Dictionary<string, RegistrationNode> IndexNodes(IReadOnlyList<RegistrationNode> nodes) =>
        nodes.GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static IEnumerable<AnalysisFinding> BuildLeakedFindings(
        IEnumerable<LeakedRegistration> leaked,
        Dictionary<string, RegistrationNode> nodeById)
    {
        foreach (var l in leaked)
        {
            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.Leaked, l.DisplayName, l.NodeId),
                Category = FindingCategory.Leaked,
                Severity = FindingSeverity.Error,
                Tier = FindingTier.Actionable,
                Title = l.DisplayName,
                Detail = $"{l.FromFramework} → {l.ToFramework}",
                Sites = [SiteFromNode(l.NodeId, l.DisplayName, l.SourceFile, l.SourceLine, nodeById)]
            };
        }
    }

    private static IEnumerable<AnalysisFinding> BuildBrokenFindings(
        IEnumerable<BrokenChain> broken,
        Dictionary<string, RegistrationNode> nodeById)
    {
        foreach (var b in broken)
        {
            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.Broken, b.DisplayName, b.NodeId),
                Category = FindingCategory.Broken,
                Severity = FindingSeverity.Error,
                Tier = FindingTier.Actionable,
                Title = b.DisplayName,
                Detail = $"→ {b.MissingDependencyType} not resolved",
                Sites = [SiteFromNode(b.NodeId, b.DisplayName, b.SourceFile, b.SourceLine, nodeById)]
            };
        }
    }

    private static IEnumerable<AnalysisFinding> BuildDuplicateFindings(
        IEnumerable<DuplicateAbstractToken> duplicates,
        RegistrationGraph graph,
        Dictionary<string, RegistrationNode> nodeById,
        FindingPolicyOptions policy,
        bool strict)
    {
        foreach (var d in duplicates)
        {
            var nodes = d.NodeIds
                .Select(id => nodeById.GetValueOrDefault(id))
                .Where(n => n != null)
                .Cast<RegistrationNode>()
                .ToList();

            if (FindingPolicy.IsIntentionalTryAddOverride(nodes, policy))
            {
                yield return new AnalysisFinding
                {
                    FindingId = FindingId(FindingCategory.Duplicate, d.AbstractTokenName, "intentional"),
                    Category = strict ? FindingCategory.Duplicate : FindingCategory.PossibleDuplicate,
                    Severity = FindingSeverity.Warn,
                    Tier = FindingTier.Intentional,
                    Title = d.AbstractTokenName,
                    Detail = "TryAdd baseline + explicit Add override",
                    Sites = nodes.Select(n => SiteFromNode(n)).ToList()
                };
                continue;
            }

            yield return new AnalysisFinding
            {
                FindingId = FindingId(
                    strict ? FindingCategory.Duplicate : FindingCategory.PossibleDuplicate,
                    d.AbstractTokenName,
                    string.Join("-", d.NodeIds.Take(2))),
                Category = strict ? FindingCategory.Duplicate : FindingCategory.PossibleDuplicate,
                Severity = FindingSeverity.Warn,
                Tier = FindingTier.Actionable,
                Title = d.AbstractTokenName,
                Detail = strict
                    ? $"registered {d.NodeIds.Count}× (may indicate leaked migration state)"
                    : $"registered {d.NodeIds.Count}× (homonym or unresolved type identity)",
                Sites = d.NodeIds
                    .Select(id => nodeById.GetValueOrDefault(id))
                    .Where(n => n != null)
                    .Select(n => SiteFromNode(n!))
                    .ToList()
            };
        }
    }

    private static IEnumerable<AnalysisFinding> BuildUnresolvedFindings(
        RegistrationGraph graph,
        Dictionary<string, RegistrationNode> nodeById,
        FindingPolicyOptions policy)
    {
        foreach (var u in graph.UnresolvedInjections)
        {
            var shortName = u.DeclaredType.ShortName;
            if (!FindingPolicy.IsActionableUnresolved(shortName, u.DeclaredType.FullyQualifiedName, policy) &&
                !policy.Strict)
                continue;

            nodeById.TryGetValue(u.FromRegistrationId, out var consumer);
            var isParserLimit = consumer != null &&
                                FindingPolicy.IsParserLimitUnresolved(consumer, u, graph);
            var tier = isParserLimit && !policy.Strict
                ? FindingTier.ParserLimit
                : FindingTier.Actionable;

            if (tier == FindingTier.ParserLimit && !policy.Strict &&
                !FindingPolicy.IsActionableUnresolved(shortName, u.DeclaredType.FullyQualifiedName, policy))
                continue;

            var consumerSite = consumer != null
                ? SiteFromNode(consumer)
                : SiteFromNode(u.FromRegistrationId, u.FromRegistrationId, null, null, nodeById);

            var providerHint = consumer?.Annotations.GetValueOrDefault("pattern");
            var detail = providerHint != null
                ? $"missing {shortName} (parser_limit: provider likely {providerHint}) ({u.Reason})"
                : $"missing {shortName} ({u.Reason})";

            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.Unresolved, shortName, u.FromRegistrationId),
                Category = FindingCategory.Unresolved,
                Severity = FindingSeverity.Warn,
                Tier = tier,
                Title = shortName,
                Detail = detail,
                Sites = [consumerSite]
            };
        }
    }

    private static IEnumerable<AnalysisFinding> BuildOrphanedFindings(
        IEnumerable<OrphanedRegistration> orphaned,
        Dictionary<string, RegistrationNode> nodeById)
    {
        foreach (var o in orphaned)
        {
            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.Orphaned, o.DisplayName, o.NodeId),
                Category = FindingCategory.Orphaned,
                Severity = FindingSeverity.Warn,
                Tier = FindingTier.Actionable,
                Title = o.DisplayName,
                Sites = [SiteFromNode(o.NodeId, o.DisplayName, o.SourceFile, o.SourceLine, nodeById)]
            };
        }
    }

    private static IEnumerable<AnalysisFinding> BuildCycleFindings(
        IEnumerable<List<string>> cycles,
        RegistrationGraph graph,
        Dictionary<string, RegistrationNode> nodeById)
    {
        var index = 0;
        foreach (var cycle in cycles)
        {
            var names = cycle
                .Select(id => nodeById.GetValueOrDefault(id)?.DisplayName ?? id)
                .ToList();
            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.Cycle, names[0], index.ToString()),
                Category = FindingCategory.Cycle,
                Severity = FindingSeverity.Warn,
                Tier = FindingTier.Actionable,
                Title = string.Join(" → ", names) + $" → {names[0]}",
                Detail = "dependency cycle",
                Sites = cycle
                    .Select(id => nodeById.GetValueOrDefault(id))
                    .Where(n => n != null)
                    .Select(n => SiteFromNode(n!))
                    .ToList()
            };
            index++;
        }
    }

    private static IEnumerable<AnalysisFinding> BuildBlindSpotFindings(
        IEnumerable<BlindSpotReport> blindSpots,
        FindingPolicyOptions policy)
    {
        var index = 0;
        foreach (var b in blindSpots)
        {
            var tier = FindingPolicy.IsInformationalBlindSpot(b.Pattern, policy)
                ? FindingTier.Informational
                : FindingTier.Actionable;

            yield return new AnalysisFinding
            {
                FindingId = FindingId(FindingCategory.BlindSpot, b.Pattern, index.ToString()),
                Category = FindingCategory.BlindSpot,
                Severity = FindingSeverity.Warn,
                Tier = tier,
                Title = b.Pattern,
                Detail = b.Description,
                Sites =
                [
                    new RegistrationSite
                    {
                        RegistrationId = $"blind_spot_{index}",
                        DisplayName = b.Pattern,
                        FilePath = b.Location?.FilePath,
                        Line = b.Location?.Line
                    }
                ]
            };
            index++;
        }
    }

    private static RegistrationSite SiteFromNode(RegistrationNode node) =>
        new()
        {
            RegistrationId = node.Id,
            DisplayName = node.DisplayName,
            FullyQualifiedName = string.IsNullOrEmpty(node.AbstractToken.FullyQualifiedName)
                ? null
                : node.AbstractToken.FullyQualifiedName,
            FilePath = node.SourceLocation?.FilePath,
            Line = node.SourceLocation?.Line,
            FrameworkTags = node.FrameworkTags
        };

    private static RegistrationSite SiteFromNode(
        string nodeId,
        string displayName,
        string? filePath,
        int? line,
        Dictionary<string, RegistrationNode> nodeById)
    {
        if (nodeById.TryGetValue(nodeId, out var node))
            return SiteFromNode(node);

        return new RegistrationSite
        {
            RegistrationId = nodeId,
            DisplayName = displayName,
            FilePath = filePath,
            Line = line
        };
    }

    private static string FindingId(FindingCategory category, string title, string suffix) =>
        $"{CategorySlug(category)}_{Sanitize(title)}_{Sanitize(suffix)}";

    private static string CategorySlug(FindingCategory category) => category switch
    {
        FindingCategory.Leaked => "leaked",
        FindingCategory.Broken => "broken",
        FindingCategory.Duplicate => "duplicate",
        FindingCategory.PossibleDuplicate => "possible_duplicate",
        FindingCategory.Unresolved => "unresolved",
        FindingCategory.Orphaned => "orphaned",
        FindingCategory.Cycle => "cycle",
        FindingCategory.BlindSpot => "blind_spot",
        _ => "finding"
    };

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "x";
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c == '_').Take(32).ToArray();
        return chars.Length == 0 ? "x" : new string(chars).ToLowerInvariant();
    }
}
