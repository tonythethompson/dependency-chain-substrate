using DCS.Core.IR;

namespace DCS.Analysis;

public static class FrameworkTagger
{
    public static List<string> InferTags(
        FrameworkBoundaryModel model,
        IEnumerable<string> fileUsings,
        TypeRef? abstractToken = null)
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

        return [.. tags.OrderBy(t => t, StringComparer.Ordinal)];
    }
}
