namespace DCS.Fix;

public static class UnifiedDiffFormatter
{
    public static string Format(string relativePath, string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);
        var builder = new System.Text.StringBuilder();

        builder.AppendLine($"--- a/{NormalizePath(relativePath)}");
        builder.AppendLine($"+++ b/{NormalizePath(relativePath)}");

        if (oldLines.SequenceEqual(newLines))
            return builder.ToString();

        var start = 0;
        while (start < oldLines.Count && start < newLines.Count && oldLines[start] == newLines[start])
            start++;

        var oldEnd = oldLines.Count - 1;
        var newEnd = newLines.Count - 1;
        while (oldEnd >= start && newEnd >= start && oldLines[oldEnd] == newLines[newEnd])
        {
            oldEnd--;
            newEnd--;
        }

        var oldCount = Math.Max(0, oldEnd - start + 1);
        var newCount = Math.Max(0, newEnd - start + 1);
        builder.AppendLine($"@@ -{start + 1},{oldCount} +{start + 1},{newCount} @@");

        for (var i = start; i <= oldEnd; i++)
            builder.AppendLine('-' + oldLines[i]);

        for (var i = start; i <= newEnd; i++)
            builder.AppendLine('+' + newLines[i]);

        return builder.ToString();
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
