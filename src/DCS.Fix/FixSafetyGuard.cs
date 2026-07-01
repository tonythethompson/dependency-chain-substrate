using DCS.Analysis;

namespace DCS.Fix;

public static class FixSafetyGuard
{
    public static bool LeakedWorsened(AnalysisResult before, AnalysisResult after)
    {
        if (after.Leaked.Count > before.Leaked.Count)
            return true;

        var beforeIds = before.Leaked
            .Select(l => l.NodeId)
            .ToHashSet(StringComparer.Ordinal);

        return after.Leaked.Any(l => !beforeIds.Contains(l.NodeId));
    }

    public static bool BrokenWorsened(AnalysisResult before, AnalysisResult after)
    {
        if (after.BrokenChains.Count > before.BrokenChains.Count)
            return true;

        var beforeKeys = before.BrokenChains
            .Select(b => (b.NodeId, b.MissingDependencyType))
            .ToHashSet();

        return after.BrokenChains.Any(b => !beforeKeys.Contains((b.NodeId, b.MissingDependencyType)));
    }

    public static void VerifyBrokenNotWorsened(
        AnalysisResult before,
        AnalysisResult after,
        string repoRoot,
        IReadOnlyList<FilePatch> patches)
    {
        if (!BrokenWorsened(before, after))
            return;

        RollbackPatches(repoRoot, patches);

        throw new InvalidOperationException(
            $"Fix rolled back: BROKEN count would increase ({before.BrokenChains.Count} → {after.BrokenChains.Count}).");
    }

    public static void VerifyApplyGuards(
        AnalysisResult before,
        AnalysisResult after,
        string repoRoot,
        IReadOnlyList<FilePatch> patches)
    {
        if (LeakedWorsened(before, after) || BrokenWorsened(before, after))
        {
            RollbackPatches(repoRoot, patches);

            if (LeakedWorsened(before, after))
            {
                throw new InvalidOperationException(
                    $"Fix rolled back: LEAKED count would increase ({before.Leaked.Count} → {after.Leaked.Count}).");
            }

            throw new InvalidOperationException(
                $"Fix rolled back: BROKEN count would increase ({before.BrokenChains.Count} → {after.BrokenChains.Count}).");
        }
    }

    public static void VerifyLeakedNotWorsened(
        AnalysisResult before,
        AnalysisResult after,
        string repoRoot,
        IReadOnlyList<FilePatch> patches)
    {
        if (!LeakedWorsened(before, after))
            return;

        RollbackPatches(repoRoot, patches);

        var newLeaks = after.Leaked
            .Where(l => before.Leaked.All(b => b.NodeId != l.NodeId))
            .Select(l => l.DisplayName)
            .Take(5)
            .ToList();

        var detail = newLeaks.Count > 0
            ? $" New leakage: {string.Join(", ", newLeaks)}."
            : string.Empty;

        throw new InvalidOperationException(
            $"Fix rolled back: LEAKED count would increase ({before.Leaked.Count} → {after.Leaked.Count}).{detail}");
    }

    public static void RollbackPatches(string repoRoot, IReadOnlyList<FilePatch> patches)
    {
        foreach (var patch in patches)
        {
            var absolutePath = Path.IsPathRooted(patch.RelativePath)
                ? patch.RelativePath
                : Path.Combine(repoRoot, patch.RelativePath);
            File.WriteAllText(absolutePath, patch.OriginalContent);
        }
    }
}
