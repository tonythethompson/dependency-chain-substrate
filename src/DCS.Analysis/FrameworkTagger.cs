using DCS.Core.IR;

namespace DCS.Analysis;

public static class FrameworkTagger
{
    public static List<string> InferTags(
        FrameworkBoundaryModel model,
        IEnumerable<string> fileUsings,
        TypeRef? abstractToken = null,
        string? sourceFilePath = null)
    {
        model ??= FrameworkBoundaryModel.Default;
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var usingDirective in fileUsings)
        {
            if (string.IsNullOrWhiteSpace(usingDirective))
                continue;

            var tag = model.GetTagForUsing(usingDirective);
            if (tag != null)
                tags.Add(tag);
        }

        if (!string.IsNullOrEmpty(abstractToken?.Namespace))
        {
            var ns = abstractToken.Namespace.EndsWith('.')
                ? abstractToken.Namespace
                : abstractToken.Namespace + ".";
            var typeTag = model.GetTagForNamespace(ns);
            if (typeTag != null)
                tags.Add(typeTag);
        }

        if (!string.IsNullOrEmpty(sourceFilePath))
        {
            foreach (var pathTag in InferTagsFromFilePath(sourceFilePath))
                tags.Add(pathTag);
        }

        return [.. tags.OrderBy(t => t, StringComparer.Ordinal)];
    }

    internal static IEnumerable<string> InferTagsFromFilePath(string sourceFilePath)
    {
        var path = sourceFilePath.Replace('\\', '/').ToLowerInvariant();
        if (path.Contains("/trackdub.app.avalonia/", StringComparison.Ordinal) ||
            path.Contains("app.axaml.cs", StringComparison.Ordinal))
            yield return "avalonia";

        if (path.Contains("/trackdub.app/", StringComparison.Ordinal) &&
            !path.Contains("avalonia", StringComparison.Ordinal))
            yield return "winui";
    }
}
