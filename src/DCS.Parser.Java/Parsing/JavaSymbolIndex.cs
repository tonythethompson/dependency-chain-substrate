using DCS.Core.IR;

namespace DCS.Parser.Java.Parsing;

public sealed class JavaSymbolIndex
{
    private readonly Dictionary<(string ModuleId, SourceSetKind SourceSet, string Fqn), JavaTypeInfo> _types = new();
    private readonly Dictionary<string, List<(string ModuleId, SourceSetKind SourceSet, string Fqn)>> _shortNameIndex = new(StringComparer.Ordinal);
    private readonly List<JavaCompilationUnit> _units = [];

    public IReadOnlyList<JavaCompilationUnit> Units => _units;

    public void Add(JavaCompilationUnit unit)
    {
        _units.Add(unit);
        foreach (var type in unit.Types)
        {
            var fqn = string.IsNullOrEmpty(unit.PackageName)
                ? type.SimpleName
                : $"{unit.PackageName}.{type.SimpleName}";

            var key = (unit.ModuleId, unit.SourceSet, fqn);
            if (_types.ContainsKey(key))
                continue;

            var info = new JavaTypeInfo
            {
                Fqn = fqn,
                SimpleName = type.SimpleName,
                ModuleId = unit.ModuleId,
                SourceSet = unit.SourceSet,
                Unit = unit,
                Declaration = type,
                Supertypes = type.ExtendsAndImplements.ToList()
            };

            _types[key] = info;

            if (!_shortNameIndex.TryGetValue(type.SimpleName, out var list))
            {
                list = [];
                _shortNameIndex[type.SimpleName] = list;
            }

            if (!list.Any(e => e.Fqn == fqn && e.ModuleId == unit.ModuleId && e.SourceSet == unit.SourceSet))
                list.Add((unit.ModuleId, unit.SourceSet, fqn));
        }
    }

    public bool HasType(string fqn) =>
        _types.Keys.Any(k => string.Equals(k.Fqn, fqn, StringComparison.Ordinal));

    public JavaTypeInfo? GetType(string moduleId, SourceSetKind sourceSet, string fqn) =>
        _types.GetValueOrDefault((moduleId, sourceSet, fqn));

    public JavaTypeInfo? FindUnique(string moduleId, SourceSetKind sourceSet, string simpleOrFqn)
    {
        if (simpleOrFqn.Contains('.'))
            return GetType(moduleId, sourceSet, simpleOrFqn);

        if (!_shortNameIndex.TryGetValue(simpleOrFqn, out var matches))
            return null;

        var filtered = matches.Where(m => m.ModuleId == moduleId && m.SourceSet == sourceSet).ToList();
        if (filtered.Count == 1)
            return GetType(filtered[0].ModuleId, filtered[0].SourceSet, filtered[0].Fqn);

        return null;
    }

    public IReadOnlyList<JavaTypeInfo> AllTypesIn(string moduleId, SourceSetKind sourceSet) =>
        _types.Values.Where(t => t.ModuleId == moduleId && t.SourceSet == sourceSet).ToList();

    public bool IsAmbiguousShortName(string simpleName, string moduleId, SourceSetKind sourceSet)
    {
        if (!_shortNameIndex.TryGetValue(simpleName, out var matches))
            return false;

        return matches.Count(m => m.ModuleId == moduleId && m.SourceSet == sourceSet) > 1;
    }

    public bool ExtendsRepository(JavaTypeInfo typeInfo)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return ExtendsRepositoryRecursive(typeInfo, visited);
    }

    private bool ExtendsRepositoryRecursive(JavaTypeInfo info, HashSet<string> visited)
    {
        if (!visited.Add(info.Fqn))
            return false;

        foreach (var superText in info.Supertypes)
        {
            var simple = superText.Contains('<') ? superText[..superText.IndexOf('<')] : superText;
            simple = simple.Contains('.') ? simple[(simple.LastIndexOf('.') + 1)..] : simple;

            if (IsSpringDataBase(simple))
                return true;

            var super = FindUnique(info.ModuleId, info.SourceSet, superText);
            if (super != null && ExtendsRepositoryRecursive(super, visited))
                return true;
        }

        return false;
    }

    private static bool IsSpringDataBase(string simpleName) =>
        simpleName is "Repository" or "JpaRepository" or "CrudRepository" or "PagingAndSortingRepository";
}

public sealed class JavaTypeInfo
{
    public required string Fqn { get; init; }
    public required string SimpleName { get; init; }
    public required string ModuleId { get; init; }
    public SourceSetKind SourceSet { get; init; }
    public required JavaCompilationUnit Unit { get; init; }
    public required JavaTypeDeclaration Declaration { get; init; }
    public List<string> Supertypes { get; init; } = [];
}
