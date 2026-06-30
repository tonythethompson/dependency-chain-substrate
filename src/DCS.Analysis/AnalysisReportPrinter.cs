namespace DCS.Analysis;

public static class AnalysisReportPrinter
{
    public static void Print(AnalysisReport report, TextWriter writer, ReportVerbosity verbosity, bool verboseBlindSpots)
    {
        writer.WriteLine();
        writer.WriteLine("=== DCS Analysis Report ===");
        if (report.CommitSha != null)
            writer.WriteLine($"Commit: {report.CommitSha}");
        if (report.ContextId != null)
            writer.WriteLine($"Context: {report.ContextId}");
        if (report.TargetFramework != null)
            writer.WriteLine($"Target framework: {report.TargetFramework}");
        writer.WriteLine($"Nodes: {report.TotalNodes}  Edges: {report.TotalEdges}");
        writer.WriteLine();

        if (report.Metrics != null)
            PrintMetrics(report.Metrics, writer);

        if (verbosity == ReportVerbosity.Summary)
        {
            PrintSummaryLine(report, writer);
            return;
        }

        PrintCategory(writer, report, FindingCategory.Leaked, "LEAKED");
        PrintCategory(writer, report, FindingCategory.Broken, "BROKEN CHAINS");
        PrintCategory(writer, report, FindingCategory.Duplicate, "DUPLICATE REGISTRATIONS");
        PrintCategory(writer, report, FindingCategory.PossibleDuplicate, "POSSIBLE DUPLICATES");
        PrintCategory(writer, report, FindingCategory.Unresolved, "UNRESOLVED DEPENDENCIES");
        PrintCategory(writer, report, FindingCategory.Orphaned, "ORPHANED");
        PrintCategory(writer, report, FindingCategory.Cycle, "CYCLES");
        PrintBlindSpots(writer, report, verboseBlindSpots);

        writer.WriteLine();
        PrintSummaryLine(report, writer);
    }

    public static void PrintMultiContext(MultiContextAnalysisReport multi, TextWriter writer, ReportVerbosity verbosity)
    {
        writer.WriteLine();
        writer.WriteLine("=== DCS Multi-Context Analysis Summary ===");
        if (multi.CommitSha != null)
            writer.WriteLine($"Commit: {multi.CommitSha}");
        writer.WriteLine();
        writer.WriteLine($"{"Context",-30} {"Errors",8} {"Dup",6} {"Poss",6} {"Unres",6} {"Orph",6} {"Blind",6}");
        foreach (var ctx in multi.ContextReports)
        {
            var s = ctx.Summary;
            writer.WriteLine(
                $"{ctx.ContextId ?? "unknown",-30} {(s.HasErrors ? "yes" : "no"),8} {s.DuplicateCount,6} {s.PossibleDuplicateCount,6} {s.UnresolvedCount,6} {s.OrphanedCount,6} {s.BlindSpotCount,6}");
        }

        if (verbosity == ReportVerbosity.Summary)
            return;

        foreach (var ctx in multi.ContextReports)
        {
            writer.WriteLine();
            writer.WriteLine($"--- Context: {ctx.ContextId} ---");
            Print(ctx, writer, verbosity, verboseBlindSpots: false);
        }
    }

    public static void PrintMetrics(ExtractionQualityMetrics metrics, TextWriter writer)
    {
        writer.WriteLine("--- EXTRACTION QUALITY ---");
        writer.WriteLine($"  Semantic type resolution:     {metrics.SemanticTypeResolutionRate:P1} ({metrics.ResolvedRegistrations}/{metrics.SyntaxCandidateRegistrations})");
        writer.WriteLine($"  Registration API verification: {metrics.RegistrationApiVerificationRate:P1} ({metrics.VerifiedRegistrations}/{metrics.SyntaxCandidateRegistrations})");
        writer.WriteLine($"  Project scope completeness:    {metrics.ProjectScopeCompletenessRate:P1} ({metrics.CompleteCompositionScopeCount}/{metrics.CompositionScopeCount})");
        writer.WriteLine();
    }

    private static void PrintCategory(TextWriter writer, AnalysisReport report, FindingCategory category, string title)
    {
        var findings = report.Findings.Where(f => f.Category == category).ToList();
        writer.WriteLine($"--- {title} ({findings.Count}) ---");
        foreach (var f in findings)
            PrintFinding(writer, f, category);
    }

    private static void PrintBlindSpots(TextWriter writer, AnalysisReport report, bool verboseBlindSpots)
    {
        var shown = report.Findings.Where(f => f.Category == FindingCategory.BlindSpot).ToList();
        var informationalCount = report.Summary.InformationalCount;
        writer.WriteLine($"--- BLIND SPOTS ({shown.Count}) ---");
        foreach (var f in shown)
            PrintFinding(writer, f, FindingCategory.BlindSpot);

        if (!verboseBlindSpots && informationalCount > 0)
            writer.WriteLine($"  (informational: {informationalCount} factory_lambda/extension_method registrations not listed; use --verbose-blind-spots)");
    }

    private static void PrintFinding(TextWriter writer, AnalysisFinding finding, FindingCategory category)
    {
        var tag = finding.Severity == FindingSeverity.Error ? "ERROR" : "WARN";
        var label = CategoryLabel(category);

        writer.WriteLine($"  [{tag}] {label}: {finding.Title}");
        if (!string.IsNullOrEmpty(finding.Detail))
            writer.WriteLine($"          {finding.Detail}");

        foreach (var site in finding.Sites)
        {
            var fqn = site.FullyQualifiedName != null ? $"  {site.FullyQualifiedName}" : string.Empty;
            writer.WriteLine($"    - {FormatSite(site)}{fqn}");
        }

        if (finding.Tier != FindingTier.Actionable)
            writer.WriteLine($"    tier: {TierLabel(finding.Tier)}");
    }

    private static void PrintSummaryLine(AnalysisReport report, TextWriter writer)
    {
        var s = report.Summary;
        writer.WriteLine(
            $"SUMMARY: {(s.HasErrors ? "ERRORS FOUND" : "no errors")} | " +
            $"{s.LeakedCount} leaked | {s.BrokenCount} broken | " +
            $"{s.DuplicateCount} duplicate | {s.PossibleDuplicateCount} possible duplicate | " +
            $"{s.UnresolvedCount} unresolved | {s.OrphanedCount} orphaned | " +
            $"{s.BlindSpotCount} blind spots");
        writer.WriteLine(
            $"TIERS: {s.ActionableCount} actionable, {s.InformationalCount} informational, " +
            $"{s.ParserLimitCount} parser_limit, {s.IntentionalCount} intentional");
    }

    private static string FormatSite(RegistrationSite site) =>
        site.FilePath == null
            ? site.DisplayName
            : site.Line == null
                ? $"{site.FilePath}  {site.DisplayName}"
                : $"{site.FilePath}:{site.Line}  {site.DisplayName}";

    private static string CategoryLabel(FindingCategory category) => category switch
    {
        FindingCategory.Leaked => "LEAKED",
        FindingCategory.Broken => "BROKEN",
        FindingCategory.Duplicate => "DUPLICATE",
        FindingCategory.PossibleDuplicate => "POSSIBLE DUPLICATE",
        FindingCategory.Unresolved => "UNRESOLVED DEPENDENCY",
        FindingCategory.Orphaned => "ORPHANED",
        FindingCategory.Cycle => "CYCLE",
        FindingCategory.BlindSpot => "BLIND SPOT",
        _ => category.ToString().ToUpperInvariant()
    };

    private static string TierLabel(FindingTier tier) => tier switch
    {
        FindingTier.Actionable => "actionable",
        FindingTier.Informational => "informational",
        FindingTier.ParserLimit => "parser_limit",
        FindingTier.Intentional => "intentional",
        _ => tier.ToString().ToLowerInvariant()
    };
}
