using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Parser.Java.Naming;
using DCS.Parser.Java.Parsing;

namespace DCS.Parser.Java.Discovery;

internal sealed class SpringAppContext
{
    public required string ContextId { get; init; }
    public required string EntryRootFqn { get; init; }
    public required string ModuleId { get; init; }
    public SourceSetKind SourceSet { get; init; }
    public HashSet<string> ScanPackages { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> RepositoryPackages { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> ImportedConfigFqns { get; init; } = new(StringComparer.Ordinal);
    public bool ScanRulesDegraded { get; set; }
    public bool ProgrammaticRegistrationDetected { get; set; }
    public List<ParseDiagnostic> Diagnostics { get; } = [];
}

internal static class SpringContextDiscovery
{
    private static readonly HashSet<string> Stereotypes = new(StringComparer.Ordinal)
    {
        "Component", "Service", "Repository", "Controller", "Configuration", "SpringBootApplication", "Named"
    };

    public static List<SpringAppContext> Discover(JavaSymbolIndex index, IReadOnlyList<string>? contextRootFilter)
    {
        var contexts = new List<SpringAppContext>();

        foreach (var info in index.Units.SelectMany(u => u.Types.Select(t => (u, t))))
        {
            var (unit, type) = info;
            var resolver = new JavaTypeResolver(index, unit);
            var fqn = resolver.ResolveFqn(type.SimpleName);

            var isBoot = type.Annotations.Any(a => a.Is("SpringBootApplication"));
            if (!isBoot)
                continue;

            if (contextRootFilter is { Count: > 0 } &&
                !contextRootFilter.Contains(fqn, StringComparer.Ordinal))
                continue;

            var ctx = new SpringAppContext
            {
                ContextId = ContextGraph.BuildContextId(unit.ModuleId, unit.SourceSet, fqn),
                EntryRootFqn = fqn,
                ModuleId = unit.ModuleId,
                SourceSet = unit.SourceSet
            };

            var pkg = unit.PackageName ?? string.Empty;
            ctx.ScanPackages.Add(pkg);

            var boot = type.Annotations.FirstOrDefault(a => a.Is("SpringBootApplication"));
            if (boot != null)
                AddScanPackages(ctx, boot.Arguments, pkg);

            var scan = type.Annotations.FirstOrDefault(a => a.Is("ComponentScan"));
            if (scan != null)
            {
                AddScanPackages(ctx, scan.Arguments, pkg);
                DetectUnsupportedScanRules(ctx, scan.Arguments);
            }

            ProcessImports(ctx, type, unit, index, resolver);
            contexts.Add(ctx);
        }

        return contexts;
    }

    private static void AddScanPackages(SpringAppContext ctx, IReadOnlyDictionary<string, string> args, string defaultPkg)
    {
        foreach (var key in new[] { "scanBasePackages", "basePackages", "value" })
        {
            if (!args.TryGetValue(key, out var val))
                continue;

            foreach (var p in SplitPackageList(val))
            {
                if (!p.Contains("${", StringComparison.Ordinal))
                    ctx.ScanPackages.Add(p);
                else
                {
                    ctx.ScanRulesDegraded = true;
                    ctx.Diagnostics.Add(new ParseDiagnostic
                    {
                        Pattern = "component_scan_unresolved_placeholder",
                        ContextId = ctx.ContextId,
                        Description = $"Unresolved placeholder in scan package: {p}"
                    });
                }
            }
        }
    }

    private static void DetectUnsupportedScanRules(SpringAppContext ctx, IReadOnlyDictionary<string, string> args)
    {
        foreach (var key in new[] { "excludeFilters", "includeFilters", "nameGenerator", "scopeResolver" })
        {
            if (!args.ContainsKey(key))
                continue;

            ctx.ScanRulesDegraded = true;
            ctx.Diagnostics.Add(new ParseDiagnostic
            {
                Pattern = key is "nameGenerator" or "scopeResolver"
                    ? "component_scan_custom_naming"
                    : "component_scan_custom_filter",
                ContextId = ctx.ContextId,
                Description = $"Unsupported @ComponentScan attribute: {key}"
            });
        }
    }

    private static void ProcessImports(
        SpringAppContext ctx,
        JavaTypeDeclaration type,
        JavaCompilationUnit unit,
        JavaSymbolIndex index,
        JavaTypeResolver resolver)
    {
        var importAnn = type.Annotations.FirstOrDefault(a => a.Is("Import"));
        if (importAnn == null)
            return;

        if (!importAnn.Arguments.TryGetValue("value", out var raw))
            return;

        foreach (var target in SplitImportTargets(raw))
        {
            ClassifyImportTarget(ctx, target, unit, index, resolver);
        }
    }

    private static void ClassifyImportTarget(
        SpringAppContext ctx,
        string target,
        JavaCompilationUnit unit,
        JavaSymbolIndex index,
        JavaTypeResolver resolver)
    {
        var fqn = resolver.ResolveFqn(target.Trim().Trim('{', '}', '"'));
        var info = index.FindUnique(unit.ModuleId, unit.SourceSet, fqn);
        if (info == null)
        {
            ctx.ScanRulesDegraded = true;
            ctx.Diagnostics.Add(new ParseDiagnostic
            {
                Pattern = "import_unresolved",
                ContextId = ctx.ContextId,
                Description = $"Unresolved @Import target: {target}"
            });
            return;
        }

        if (ImplementsInterface(info, "ImportSelector") || ImplementsInterface(info, "DeferredImportSelector"))
        {
            ctx.ScanRulesDegraded = true;
            ctx.ProgrammaticRegistrationDetected = true;
            ctx.Diagnostics.Add(new ParseDiagnostic
            {
                Pattern = "import_selector",
                ContextId = ctx.ContextId,
                Description = $"Dynamic @Import selector: {fqn}"
            });
            return;
        }

        if (ImplementsInterface(info, "ImportBeanDefinitionRegistrar"))
        {
            ctx.ScanRulesDegraded = true;
            ctx.ProgrammaticRegistrationDetected = true;
            ctx.Diagnostics.Add(new ParseDiagnostic
            {
                Pattern = "import_bean_definition_registrar",
                ContextId = ctx.ContextId,
                Description = $"ImportBeanDefinitionRegistrar: {fqn}"
            });
            return;
        }

        ctx.ImportedConfigFqns.Add(fqn);
    }

    private static bool ImplementsInterface(JavaTypeInfo info, string ifaceSimple) =>
        info.Supertypes.Any(s =>
        {
            var simple = s.Contains('<') ? s[..s.IndexOf('<')] : s;
            simple = simple.Contains('.') ? simple[(simple.LastIndexOf('.') + 1)..] : simple;
            return string.Equals(simple, ifaceSimple, StringComparison.Ordinal);
        }) ||
        info.Declaration.ExtendsAndImplements.Any(s => s.Contains(ifaceSimple, StringComparison.Ordinal));

    public static bool IsInScanPackage(string? typePackage, SpringAppContext ctx) =>
        !string.IsNullOrEmpty(typePackage) &&
        ctx.ScanPackages.Any(root =>
            typePackage.Equals(root, StringComparison.Ordinal) ||
            typePackage.StartsWith(root + ".", StringComparison.Ordinal));

    private static IEnumerable<string> SplitPackageList(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('{'))
        {
            foreach (var part in raw.Trim('{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return part.Trim('"');
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(raw))
            yield return raw.Trim('"');
    }

    private static IEnumerable<string> SplitImportTargets(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('{'))
        {
            foreach (var part in raw.Trim('{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return part.Trim();
            yield break;
        }

        yield return raw;
    }
}

internal static class SpringDataContextDiscovery
{
    public static void Apply(JavaSymbolIndex index, SpringAppContext ctx)
    {
        foreach (var unit in index.Units.Where(u => u.ModuleId == ctx.ModuleId && u.SourceSet == ctx.SourceSet))
        {
            foreach (var type in unit.Types)
            {
                var enable = type.Annotations.FirstOrDefault(a =>
                    a.Is("EnableJpaRepositories") || a.ShortName.StartsWith("Enable", StringComparison.Ordinal) && a.ShortName.EndsWith("Repositories", StringComparison.Ordinal));
                if (enable == null)
                    continue;

                foreach (var key in new[] { "basePackages", "value" })
                {
                    if (!enable.Arguments.TryGetValue(key, out var val))
                        continue;

                    foreach (var p in val.Trim('{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        ctx.RepositoryPackages.Add(p.Trim('"'));
                }

                if (ctx.RepositoryPackages.Count == 0 && !string.IsNullOrEmpty(unit.PackageName))
                    ctx.RepositoryPackages.Add(unit.PackageName);
            }
        }

        if (ctx.RepositoryPackages.Count == 0)
        {
            foreach (var pkg in ctx.ScanPackages)
                ctx.RepositoryPackages.Add(pkg);
        }
    }

    public static bool IsInRepositoryPackage(string? typePackage, SpringAppContext ctx) =>
        !string.IsNullOrEmpty(typePackage) &&
        ctx.RepositoryPackages.Any(root =>
            typePackage.Equals(root, StringComparison.Ordinal) ||
            typePackage.StartsWith(root + ".", StringComparison.Ordinal));
}

internal static class ProgrammaticRegistrationDetector
{
    private static readonly string[] Markers =
    [
        "ImportSelector",
        "DeferredImportSelector",
        "ImportBeanDefinitionRegistrar",
        "BeanDefinitionRegistryPostProcessor"
    ];

    public static void Scan(JavaSymbolIndex index, SpringAppContext ctx)
    {
        foreach (var info in index.AllTypesIn(ctx.ModuleId, ctx.SourceSet))
        {
            foreach (var marker in Markers)
            {
                if (!info.Declaration.ExtendsAndImplements.Any(s => s.Contains(marker, StringComparison.Ordinal)) &&
                    !info.Supertypes.Any(s => s.Contains(marker, StringComparison.Ordinal)))
                    continue;

                ctx.ProgrammaticRegistrationDetected = true;
                ctx.ScanRulesDegraded = true;
                ctx.Diagnostics.Add(new ParseDiagnostic
                {
                    Pattern = "programmatic_registration",
                    ContextId = ctx.ContextId,
                    Description = $"Programmatic registration marker: {marker} in {info.Fqn}"
                });
            }
        }
    }
}
