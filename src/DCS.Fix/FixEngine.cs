using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Fix;

public sealed record FilePatch(string RelativePath, string OriginalContent, string UpdatedContent);

public sealed record FixResult(
    IReadOnlyList<DuplicateFixProposal> Proposals,
    IReadOnlyList<FilePatch> Patches,
    bool Applied);

public static class FixEngine
{
    public static FixResult BuildDuplicateFixes(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? tokenFilter = null)
    {
        var proposals = DuplicateFixPlanner.Plan(graph, analysis, tokenFilter);
        if (proposals.Count == 0)
            return new FixResult([], [], false);

        var patchesByPath = new Dictionary<string, FilePatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var proposal in proposals.OrderByDescending(p => p.Line))
        {
            var relativePath = proposal.RelativeFilePath;
            var absolutePath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(repoRoot, relativePath);

            if (!File.Exists(absolutePath))
                throw new InvalidOperationException($"Registration file not found: {absolutePath}");

            var current = patchesByPath.TryGetValue(relativePath, out var existing)
                ? existing.UpdatedContent
                : File.ReadAllText(absolutePath);

            var updated = RegistrationStatementRemover.TryRemove(
                current,
                proposal.Line,
                proposal.AbstractTokenName);

            if (updated == null)
            {
                throw new InvalidOperationException(
                    $"Could not locate registration statement for {proposal.AbstractTokenName} " +
                    $"at {relativePath}:{proposal.Line}.");
            }

            var original = existing?.OriginalContent ?? current;
            patchesByPath[relativePath] = new FilePatch(relativePath, original, updated);
        }

        return new FixResult(proposals, patchesByPath.Values.ToList(), false);
    }

    public static FixResult ApplyDuplicateFixes(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? tokenFilter = null,
        bool forceDirtyTree = false)
    {
        if (!forceDirtyTree && !GitWorkingTreeGuard.IsClean(repoRoot))
        {
            throw new InvalidOperationException(
                "Working tree is not clean. Commit or stash changes, or pass --force to apply anyway.");
        }

        var preview = BuildDuplicateFixes(repoRoot, graph, analysis, tokenFilter);
        foreach (var patch in preview.Patches)
        {
            var absolutePath = Path.IsPathRooted(patch.RelativePath)
                ? patch.RelativePath
                : Path.Combine(repoRoot, patch.RelativePath);
            File.WriteAllText(absolutePath, patch.UpdatedContent);
        }

        return preview with { Applied = true };
    }

    public static string FormatPreview(FixResult result)
    {
        if (result.Patches.Count == 0)
            return "No duplicate registration fixes available.";

        var builder = new System.Text.StringBuilder();
        foreach (var proposal in result.Proposals)
        {
            builder.AppendLine(
                $"FIX DUPLICATE {proposal.AbstractTokenName}: remove {proposal.RelativeFilePath}:{proposal.Line} " +
                $"(keep {proposal.Keep.SourceLocation?.FilePath}:{proposal.Keep.SourceLocation?.Line})");
        }

        builder.AppendLine();
        foreach (var patch in result.Patches)
            builder.Append(UnifiedDiffFormatter.Format(patch.RelativePath, patch.OriginalContent, patch.UpdatedContent));

        return builder.ToString();
    }

    public static OrphanedFixResult BuildOrphanedFixes(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? displayNameFilter = null)
    {
        var proposals = OrphanedFixPlanner.Plan(graph, analysis, displayNameFilter);
        if (proposals.Count == 0)
            return new OrphanedFixResult([], [], false);

        var patchesByPath = new Dictionary<string, FilePatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var proposal in proposals.OrderByDescending(p => p.Line))
        {
            var relativePath = proposal.RelativeFilePath;
            var absolutePath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(repoRoot, relativePath);

            if (!File.Exists(absolutePath))
                throw new InvalidOperationException($"Registration file not found: {absolutePath}");

            var current = patchesByPath.TryGetValue(relativePath, out var existing)
                ? existing.UpdatedContent
                : File.ReadAllText(absolutePath);

            var updated = RegistrationStatementRemover.TryRemove(
                current,
                proposal.Line,
                proposal.DisplayName);

            if (updated == null)
            {
                throw new InvalidOperationException(
                    $"Could not locate registration statement for {proposal.DisplayName} " +
                    $"at {relativePath}:{proposal.Line}.");
            }

            var original = existing?.OriginalContent ?? current;
            patchesByPath[relativePath] = new FilePatch(relativePath, original, updated);
        }

        return new OrphanedFixResult(proposals, patchesByPath.Values.ToList(), false);
    }

    public static OrphanedFixResult ApplyOrphanedFixes(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? displayNameFilter = null,
        bool forceDirtyTree = false)
    {
        if (!forceDirtyTree && !GitWorkingTreeGuard.IsClean(repoRoot))
        {
            throw new InvalidOperationException(
                "Working tree is not clean. Commit or stash changes, or pass --force to apply anyway.");
        }

        var preview = BuildOrphanedFixes(repoRoot, graph, analysis, displayNameFilter);
        foreach (var patch in preview.Patches)
        {
            var absolutePath = Path.IsPathRooted(patch.RelativePath)
                ? patch.RelativePath
                : Path.Combine(repoRoot, patch.RelativePath);
            File.WriteAllText(absolutePath, patch.UpdatedContent);
        }

        return preview with { Applied = true };
    }

    public static string FormatOrphanedPreview(
        OrphanedFixResult result,
        OrphanedFixMeasurementReport measurement)
    {
        if (result.Patches.Count == 0)
            return measurement.FormatSummary() + Environment.NewLine + "No orphaned registration fixes available.";

        var builder = new System.Text.StringBuilder();
        builder.AppendLine(measurement.FormatSummary());
        builder.AppendLine(result.Applied
            ? "=== Orphaned Fix Applied ==="
            : "=== Orphaned Fix Preview (no files written) ===");
        foreach (var proposal in result.Proposals)
        {
            builder.AppendLine(
                $"FIX ORPHANED {proposal.DisplayName}: remove {proposal.RelativeFilePath}:{proposal.Line}");
        }

        builder.AppendLine();
        foreach (var patch in result.Patches)
            builder.Append(UnifiedDiffFormatter.Format(patch.RelativePath, patch.OriginalContent, patch.UpdatedContent));

        return builder.ToString();
    }
}
