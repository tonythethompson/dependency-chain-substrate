using DCS.Core.IR;

namespace DCS.Parser.Java.Parsing;

public sealed class JavaTypeResolver
{
    private readonly JavaSymbolIndex _index;
    private readonly string? _package;
    private readonly IReadOnlyList<string> _imports;

    public JavaTypeResolver(JavaSymbolIndex index, JavaCompilationUnit unit)
    {
        _index = index;
        _package = unit.PackageName;
        _imports = unit.Imports;
    }

    public TypeRef ResolveType(string typeText, string language = "java")
    {
        var trimmed = typeText.Trim();
        var genericArgs = new List<TypeRef>();
        string baseName = trimmed;

        var angle = trimmed.IndexOf('<');
        if (angle >= 0)
        {
            baseName = trimmed[..angle].Trim();
            var inner = trimmed[(angle + 1)..trimmed.LastIndexOf('>')];
            foreach (var part in SplitGenericArgs(inner))
                genericArgs.Add(ResolveType(part, language));
        }

        var fqn = ResolveFqn(baseName);
        var shortName = fqn.Contains('.') ? fqn[(fqn.LastIndexOf('.') + 1)..] : fqn;
        var ns = fqn.Contains('.') ? fqn[..fqn.LastIndexOf('.')] : null;

        return new TypeRef
        {
            FullyQualifiedName = fqn,
            ShortName = shortName,
            Namespace = ns,
            Language = language,
            IsGeneric = genericArgs.Count > 0,
            TypeArguments = genericArgs
        };
    }

    public string ResolveFqn(string typeText)
    {
        var simple = typeText.Trim();
        if (simple.Contains('.'))
            return simple;

        foreach (var import in _imports)
        {
            if (import.EndsWith(".*", StringComparison.Ordinal))
            {
                var pkg = import[..^2];
                var candidate = $"{pkg}.{simple}";
                if (_index.HasType(candidate))
                    return candidate;
                continue;
            }

            if (import.EndsWith($".{simple}", StringComparison.Ordinal))
                return import;
        }

        if (!string.IsNullOrEmpty(_package))
            return $"{_package}.{simple}";

        return simple;
    }

    private static IEnumerable<string> SplitGenericArgs(string inner)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '<') depth++;
            else if (c == '>')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return inner[start..i].Trim();
                start = i + 1;
            }
        }

        if (start < inner.Length)
            yield return inner[start..].Trim();
    }
}
