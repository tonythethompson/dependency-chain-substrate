using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Parser.Java.Discovery;
using DCS.Parser.Java.Naming;
using DCS.Parser.Java.Parsing;

namespace DCS.Parser.Java;

internal sealed class SpringGraphBuilder
{
    private readonly JavaParseOptions _options;
    private readonly List<ParseDiagnostic> _globalDiagnostics = [];

    public SpringGraphBuilder(JavaParseOptions options) => _options = options;

    public ParseResult Build(
        IReadOnlyList<(string path, string content)> sourceFiles,
        string rootPath,
        string? commitSha)
    {
        using var javaParser = new TreeSitterJavaParser();
        var index = new JavaSymbolIndex();

        foreach (var (path, content) in sourceFiles)
        {
            var moduleId = JavaSourceMetadata.InferModuleId(path, rootPath);
            var sourceSet = JavaSourceMetadata.InferSourceSet(path);
            if (!JavaSourceMetadata.MatchesFilter(sourceSet, _options.SourceSets))
                continue;

            try
            {
                var (_, root) = javaParser.Parse(content);
                var unit = JavaCompilationUnitBuilder.Build(path, moduleId, sourceSet, content, root);
                index.Add(unit);
            }
            catch (Exception ex)
            {
                _globalDiagnostics.Add(new ParseDiagnostic
                {
                    Pattern = "syntax_error",
                    Description = ex.Message,
                    Location = new SourceRef { FilePath = path }
                });
            }
        }

        var contexts = SpringContextDiscovery.Discover(index, _options.ContextRoots);
        if (contexts.Count == 0)
        {
            return new ParseResult
            {
                Diagnostics = _globalDiagnostics,
                ContextGraphs = []
            };
        }

        var contextGraphs = new List<ContextGraph>();

        foreach (var ctx in contexts)
        {
            SpringDataContextDiscovery.Apply(index, ctx);
            ProgrammaticRegistrationDetector.Scan(index, ctx);

            var graph = BuildContextGraph(index, ctx, commitSha, rootPath);
            contextGraphs.Add(new ContextGraph
            {
                ContextId = ctx.ContextId,
                EntryRoot = TypeRef.FromQualifiedName(ctx.EntryRootFqn) with { Language = "java" },
                ModuleId = ctx.ModuleId,
                SourceSet = ctx.SourceSet,
                Graph = graph
            });

            _globalDiagnostics.AddRange(ctx.Diagnostics);
        }

        return new ParseResult
        {
            ContextGraphs = contextGraphs.OrderBy(c => c.ContextId, StringComparer.Ordinal).ToList(),
            Diagnostics = _globalDiagnostics.OrderBy(d => d.Pattern, StringComparer.Ordinal).ToList()
        };
    }

    private RegistrationGraph BuildContextGraph(JavaSymbolIndex index, SpringAppContext ctx, string? commitSha, string rootPath)
    {
        var nodes = new List<RegistrationNode>();
        var blindSpots = new List<BlindSpotReport>();
        var factoryProvenance = new List<FactoryProvenance>();
        var injectionSites = new List<InjectionSite>();
        blindSpots.AddRange(SpringAutoConfigurationScanner.Scan(rootPath, ctx.ContextId));

        foreach (var info in index.AllTypesIn(ctx.ModuleId, ctx.SourceSet))
        {
            var unit = info.Unit;
            var resolver = new JavaTypeResolver(index, unit);
            var type = info.Declaration;
            var typeFqn = info.Fqn;
            var typePackage = unit.PackageName;

            if (info.Declaration.IsInterface && index.ExtendsRepository(info))
            {
                var repoNode = BuildRepositoryNode(info, ctx, resolver, unit);
                if (repoNode != null)
                    nodes.Add(repoNode);
                continue;
            }

            if (type.IsInterface)
                continue;

            var stereotype = type.Annotations.FirstOrDefault(a =>
                a.Is("Component") || a.Is("Service") || a.Is("Repository") || a.Is("Controller") || a.Is("Named"));
            var isConfiguration = type.Annotations.Any(a => a.Is("Configuration") || a.Is("SpringBootApplication"));
            var hasStereotype = stereotype != null || isConfiguration;

            if (hasStereotype)
            {
                AddConditionalBlindSpots(type, unit, blindSpots);
                var node = BuildStereotypeNode(info, ctx, resolver, unit, stereotype, isConfiguration);
                if (node != null)
                    nodes.Add(node);
            }

            if (isConfiguration || type.Annotations.Any(a => a.Is("Component") || a.Is("SpringBootApplication")))
            {
                foreach (var method in type.Methods.Where(m => m.Annotations.Any(a => a.Is("Bean"))))
                {
                    AddConditionalBlindSpots(type, unit, blindSpots, method);
                    var beanNode = BuildBeanNode(info, method, ctx, resolver, unit);
                    if (beanNode != null)
                    {
                        nodes.Add(beanNode);
                        factoryProvenance.Add(new FactoryProvenance
                        {
                            ProductRegistrationId = beanNode.Id,
                            OwnerTypeFqn = typeFqn,
                            OwnerRegistrationId = method.IsStatic
                                ? null
                                : nodes.FirstOrDefault(n => n.ExposedType?.FullyQualifiedName == typeFqn)?.Id,
                            FactoryMethod = method.Name,
                            InvocationMode = method.IsStatic ? FactoryInvocationMode.Static : FactoryInvocationMode.Instance
                        });

                        foreach (var param in method.Parameters)
                        {
                            injectionSites.Add(new InjectionSite(
                                beanNode.Id,
                                resolver.ResolveType(param.TypeText),
                                Mechanism.FactoryParameter,
                                param.Name,
                                param.Annotations,
                                new SourceRef { FilePath = unit.FilePath, Line = method.Line }));
                        }
                    }
                }
            }

            var classNode = nodes.LastOrDefault(n => n.ExposedType?.FullyQualifiedName == typeFqn);
            if (classNode != null)
                CollectClassInjections(type, classNode.Id, unit, resolver, injectionSites, blindSpots);
        }

        var (edges, conditional, unresolved) = ConservativeEdgeResolver.Resolve(nodes, injectionSites, ctx.ContextId);

        if (ctx.ProgrammaticRegistrationDetected || ctx.ScanRulesDegraded)
            blindSpots.Add(new BlindSpotReport
            {
                Pattern = ctx.ProgrammaticRegistrationDetected ? "programmatic_registration" : "scan_rules_degraded",
                Description = "Context completeness degraded due to dynamic or unevaluable registration rules."
            });

        var sortedNodes = nodes
            .OrderBy(n => n.ModuleId, StringComparer.Ordinal)
            .ThenBy(n => n.ExposedType?.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(n => n.SourceLocation?.FilePath, StringComparer.Ordinal)
            .ThenBy(n => n.SourceLocation?.Line)
            .ToList();

        return new RegistrationGraph
        {
            ParserVersion = SpringStaticParser.ParserVersion,
            SourceLanguage = "java",
            CommitSha = commitSha,
            Nodes = sortedNodes,
            Edges = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            BlindSpots = blindSpots,
            FactoryProvenance = factoryProvenance,
            ConditionalInjections = conditional,
            UnresolvedInjections = unresolved,
            Metadata = new Dictionary<string, string>
            {
                ["analysis_version"] = SpringStaticParser.ParserVersion,
                ["context_id"] = ctx.ContextId,
                ["registration_count"] = sortedNodes.Count.ToString(),
                ["context_completeness"] = ctx.ProgrammaticRegistrationDetected || ctx.ScanRulesDegraded ? "degraded" : "normal"
            }
        };
    }

    private RegistrationNode? BuildStereotypeNode(
        JavaTypeInfo info,
        SpringAppContext ctx,
        JavaTypeResolver resolver,
        JavaCompilationUnit unit,
        JavaAnnotation? stereotype,
        bool isConfiguration)
    {
        var type = info.Declaration;
        var exposed = resolver.ResolveType(type.SimpleName);
        var (primary, aliases) = stereotype != null
            ? SpringBeanNameGenerator.ParseStereotypeNames(stereotype.Arguments, type.SimpleName)
            : SpringBeanNameGenerator.ParseStereotypeNames(new Dictionary<string, string>(), type.SimpleName);

        if (type.Annotations.Any(a => a.Is("Named")))
        {
            var named = type.Annotations.First(a => a.Is("Named"));
            (primary, aliases) = SpringBeanNameGenerator.ParseStereotypeNames(named.Arguments, type.SimpleName);
        }

        var membership = BuildMembership(info, ctx, MembershipEvidence.ComponentScan);
        var scope = ParseScope(type.Annotations);
        var qualifiers = ParseQualifiers(type.Annotations);

        return CreateRegistrationNode(
            exposed,
            exposed,
            primary,
            aliases,
            membership,
            scope,
            qualifiers,
            type.Annotations.Any(a => a.Is("Primary")),
            stereotype?.Is("Named") == true ? RegistrationOrigin.NamedStereotype : RegistrationOrigin.Stereotype,
            info,
            unit,
            (int)type.Node.StartPosition.Row + 1,
            type.ExtendsAndImplements.Select(t => resolver.ResolveType(t)).ToList());
    }

    private RegistrationNode? BuildBeanNode(
        JavaTypeInfo owner,
        JavaMethodDeclaration method,
        SpringAppContext ctx,
        JavaTypeResolver resolver,
        JavaCompilationUnit unit)
    {
        if (method.ReturnTypeText == null)
            return null;

        var exposed = resolver.ResolveType(method.ReturnTypeText);
        TypeRef? impl = null;
        if (method.BodyText != null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(method.BodyText, @"return\s+new\s+(\w+)");
            if (match.Success)
                impl = resolver.ResolveType(match.Groups[1].Value);
        }

        var beanAnn = method.Annotations.First(a => a.Is("Bean"));
        var (primary, aliases) = SpringBeanNameGenerator.ParseBeanNames(beanAnn.Arguments, method.Name);
        var inheritedConditions = owner.Declaration.Annotations.Concat(method.Annotations).ToList();
        var membership = BuildMembership(owner, ctx, MembershipEvidence.BeanMethod);
        if (HasConditional(inheritedConditions))
            membership = membership with { State = ReachabilityState.Conditional };

        var scope = ParseScope(method.Annotations.Concat(owner.Declaration.Annotations).ToList());

        return CreateRegistrationNode(
            exposed,
            impl ?? (exposed.FullyQualifiedName == method.ReturnTypeText ? exposed : null),
            primary,
            aliases,
            membership,
            scope,
            ParseQualifiers(method.Annotations),
            method.Annotations.Any(a => a.Is("Primary")),
            RegistrationOrigin.BeanMethod,
            owner,
            unit,
            method.Line,
            []);
    }

    private RegistrationNode? BuildRepositoryNode(
        JavaTypeInfo info,
        SpringAppContext ctx,
        JavaTypeResolver resolver,
        JavaCompilationUnit unit)
    {
        var exposed = resolver.ResolveType(info.SimpleName);
        var inRepoScan = SpringDataContextDiscovery.IsInRepositoryPackage(unit.PackageName, ctx);
        var state = inRepoScan
            ? (ctx.ScanRulesDegraded ? ReachabilityState.Candidate : ReachabilityState.StaticallyReachable)
            : ReachabilityState.Candidate;

        var membership = new ContextMembership
        {
            ContextId = ctx.ContextId,
            State = state,
            Evidence = MembershipEvidence.RepositoryScan
        };

        return CreateRegistrationNode(
            exposed,
            null,
            SpringBeanNameGenerator.Decapitalize(info.SimpleName),
            [],
            membership,
            (Lifetime.Singleton, new Dictionary<string, string>()),
            [],
            false,
            RegistrationOrigin.SpringData,
            info,
            unit,
            (int)info.Declaration.Node.StartPosition.Row + 1,
            info.Declaration.ExtendsAndImplements.Select(t => resolver.ResolveType(t)).ToList(),
            Confidence.Degraded);
    }

    private ContextMembership BuildMembership(JavaTypeInfo info, SpringAppContext ctx, MembershipEvidence evidence)
    {
        var pkg = info.Unit.PackageName;
        var inScan = SpringContextDiscovery.IsInScanPackage(pkg, ctx) ||
                     ctx.ImportedConfigFqns.Contains(info.Fqn) ||
                     info.Fqn == ctx.EntryRootFqn;

        var state = inScan
            ? (ctx.ScanRulesDegraded ? ReachabilityState.Candidate : ReachabilityState.StaticallyReachable)
            : ReachabilityState.Candidate;

        if (HasConditional(info.Declaration.Annotations))
            state = ReachabilityState.Conditional;

        return new ContextMembership
        {
            ContextId = ctx.ContextId,
            State = state,
            Evidence = evidence
        };
    }

    private static bool HasConditional(IEnumerable<JavaAnnotation> annotations) =>
        annotations.Any(a =>
            a.ShortName.StartsWith("Conditional", StringComparison.Ordinal) ||
            a.Is("Profile"));

    private static (Lifetime lifetime, Dictionary<string, string> annotations) ParseScope(List<JavaAnnotation> annotations)
    {
        var scopeAnn = annotations.FirstOrDefault(a => a.Is("Scope"));
        if (scopeAnn == null)
            return (Lifetime.Singleton, new Dictionary<string, string>());

        var meta = new Dictionary<string, string>();
        var scopeValue = scopeAnn.Arguments.TryGetValue("value", out var v)
            ? v.Trim('"')
            : scopeAnn.Arguments.TryGetValue("scopeName", out var s) ? s.Trim('"') : "singleton";

        if (scopeAnn.Arguments.TryGetValue("proxyMode", out var proxy))
            meta["scoped_proxy"] = proxy;

        var lifetime = scopeValue.ToLowerInvariant() switch
        {
            "singleton" => Lifetime.Singleton,
            "prototype" => Lifetime.Prototype,
            "request" => Lifetime.Request,
            "session" => Lifetime.Session,
            "application" => Lifetime.Application,
            "websocket" => Lifetime.Session,
            _ => Lifetime.Unknown
        };

        if (lifetime == Lifetime.Unknown)
            meta["spring_scope"] = scopeValue;

        return (lifetime, meta);
    }

    private static List<QualifierConstraint> ParseQualifiers(IEnumerable<JavaAnnotation> annotations)
    {
        var result = new List<QualifierConstraint>();
        foreach (var ann in annotations)
        {
            if (ann.Is("Qualifier") || ann.Is("Named"))
            {
                var val = ann.Arguments.TryGetValue("value", out var v) ? v.Trim('"') : ann.ShortName;
                result.Add(new QualifierConstraint { Kind = ann.ShortName, Value = val });
            }
        }

        return result;
    }

    private RegistrationNode CreateRegistrationNode(
        TypeRef exposed,
        TypeRef? implementation,
        string primaryBeanName,
        List<string> beanAliases,
        ContextMembership membership,
        (Lifetime lifetime, Dictionary<string, string> scopeMeta) scope,
        List<QualifierConstraint> qualifiers,
        bool isPrimary,
        RegistrationOrigin origin,
        JavaTypeInfo info,
        JavaCompilationUnit unit,
        int line,
        List<TypeRef> aliasTypes,
        Confidence confidence = Confidence.Explicit)
    {
        var fqn = exposed.FullyQualifiedName;
        var tags = FrameworkTagger.InferTags(_options.Boundaries, unit.Imports, exposed);

        var annotations = new Dictionary<string, string>(scope.scopeMeta);
        foreach (var cond in info.Declaration.Annotations.Where(a => HasConditional([a])))
        {
            annotations["conditional_type"] = cond.ShortName;
            if (cond.Arguments.TryGetValue("name", out var n))
                annotations["conditional_key"] = n;
        }

        return new RegistrationNode
        {
            Id = RegistrationNode.ComputeId(fqn),
            InstanceId = RegistrationNode.ComputeInstanceId(fqn, unit.FilePath, line),
            DisplayName = primaryBeanName,
            AbstractToken = exposed,
            ConcreteImpl = implementation,
            ExposedType = exposed,
            ImplementationType = implementation,
            Aliases = aliasTypes,
            PrimaryBeanName = primaryBeanName,
            BeanAliases = beanAliases,
            Lifetime = scope.lifetime,
            SourceLocation = new SourceRef { FilePath = unit.FilePath, Line = line },
            ParserConfidence = membership.State == ReachabilityState.Conditional ? Confidence.Degraded : confidence,
            FrameworkTags = tags,
            Annotations = annotations,
            ContextMemberships = [membership],
            QualifierConstraints = qualifiers,
            IsPrimary = isPrimary,
            Origin = origin,
            ModuleId = info.ModuleId,
            SourceSet = info.SourceSet
        };
    }

    private static void AddConditionalBlindSpots(
        JavaTypeDeclaration type,
        JavaCompilationUnit unit,
        List<BlindSpotReport> blindSpots,
        JavaMethodDeclaration? beanMethod = null)
    {
        var line = beanMethod?.Line ?? (int)type.Node.StartPosition.Row + 1;
        var annotations = beanMethod != null
            ? type.Annotations.Concat(beanMethod.Annotations)
            : type.Annotations;

        foreach (var ann in annotations.Where(a => HasConditional([a])))
        {
            blindSpots.Add(new BlindSpotReport
            {
                Pattern = "conditional_registration",
                Description = $"Registration guarded by @{ann.ShortName}; runtime activation not evaluated.",
                Location = new SourceRef { FilePath = unit.FilePath, Line = line }
            });
        }
    }

    private static void CollectClassInjections(
        JavaTypeDeclaration type,
        string fromId,
        JavaCompilationUnit unit,
        JavaTypeResolver resolver,
        List<InjectionSite> sites,
        List<BlindSpotReport> blindSpots)
    {
        foreach (var field in type.Fields.Where(f => f.Annotations.Any(a => a.Is("Autowired") || a.Is("Inject"))))
        {
            if (field.TypeText == null)
                continue;

            if (IsUnsupportedInjectionType(field.TypeText))
            {
                blindSpots.Add(new BlindSpotReport { Pattern = BlindSpotForType(field.TypeText), Description = $"Unsupported injection type: {field.TypeText}", Location = new SourceRef { FilePath = unit.FilePath, Line = field.Line } });
                continue;
            }

            sites.Add(new InjectionSite(fromId, resolver.ResolveType(field.TypeText), Mechanism.Field, field.Name, field.Annotations,
                new SourceRef { FilePath = unit.FilePath, Line = field.Line }));
        }

        var ctors = type.Constructors;
        var explicitCtor = ctors.FirstOrDefault(c => c.Annotations.Any(a => a.Is("Autowired") || a.Is("Inject")));
        var targetCtor = explicitCtor ?? (ctors.Count == 1 ? ctors[0] : null);
        if (targetCtor == null)
            return;

        var inferred = explicitCtor == null;
        foreach (var param in targetCtor.Parameters)
        {
            if (IsUnsupportedInjectionType(param.TypeText))
            {
                blindSpots.Add(new BlindSpotReport { Pattern = BlindSpotForType(param.TypeText), Description = $"Unsupported injection type: {param.TypeText}", Location = new SourceRef { FilePath = unit.FilePath, Line = targetCtor.Line } });
                continue;
            }

            if (param.TypeText.Contains('<') && !param.TypeText.StartsWith("Optional<", StringComparison.Ordinal))
            {
                blindSpots.Add(new BlindSpotReport { Pattern = "injection_generic_qualifier", Description = "Generic-aware autowiring not resolved", Location = new SourceRef { FilePath = unit.FilePath, Line = targetCtor.Line } });
                continue;
            }

            sites.Add(new InjectionSite(fromId, resolver.ResolveType(param.TypeText), Mechanism.Constructor, param.Name, param.Annotations,
                new SourceRef { FilePath = unit.FilePath, Line = targetCtor.Line }, inferred ? Confidence.Inferred : Confidence.Explicit));
        }
    }

    private static bool IsUnsupportedInjectionType(string typeText) =>
        typeText.StartsWith("Optional<", StringComparison.Ordinal) ||
        typeText.Contains("Provider<", StringComparison.Ordinal) ||
        typeText.Contains("ObjectProvider<", StringComparison.Ordinal) ||
        typeText.StartsWith("Collection<", StringComparison.Ordinal) ||
        typeText.StartsWith("List<", StringComparison.Ordinal) ||
        typeText.StartsWith("Map<", StringComparison.Ordinal);

    private static string BlindSpotForType(string typeText)
    {
        if (typeText.StartsWith("Optional<", StringComparison.Ordinal) || typeText.Contains("Provider<", StringComparison.Ordinal))
            return "injection_optional_provider";
        if (typeText.StartsWith("Collection<", StringComparison.Ordinal) || typeText.StartsWith("Map<", StringComparison.Ordinal))
            return "injection_collection_map";
        return "injection_unsupported";
    }
}

internal sealed record InjectionSite(
    string FromRegistrationId,
    TypeRef DeclaredType,
    Mechanism Mechanism,
    string? ParameterName,
    List<JavaAnnotation> Annotations,
    SourceRef Location,
    Confidence Confidence = Confidence.Explicit);

internal static class ConservativeEdgeResolver
{
    public static (List<DependencyEdge> Edges, List<ConditionalInjection> Conditional, List<UnresolvedInjection> Unresolved) Resolve(
        List<RegistrationNode> nodes,
        List<InjectionSite> sites,
        string contextId)
    {
        var edges = new List<DependencyEdge>();
        var conditional = new List<ConditionalInjection>();
        var unresolved = new List<UnresolvedInjection>();
        var index = 0;

        foreach (var site in sites)
        {
            var candidates = FindCandidates(nodes, site, contextId);
            var unconditional = candidates.Where(c => GetState(c, contextId) == ReachabilityState.StaticallyReachable).ToList();
            var conditionalOnly = candidates.Where(c => GetState(c, contextId) == ReachabilityState.Conditional).ToList();

            if (unconditional.Count == 1)
            {
                var target = unconditional[0];
                edges.Add(new DependencyEdge
                {
                    Id = DependencyEdge.ComputeId(site.FromRegistrationId, target.Id, index++),
                    From = site.FromRegistrationId,
                    To = target.Id,
                    InjectionMechanism = site.Mechanism,
                    ParameterName = site.ParameterName,
                    ParserConfidence = site.Confidence
                });

                if (conditionalOnly.Count > 0)
                {
                    conditional.Add(new ConditionalInjection
                    {
                        Id = $"cond:{site.FromRegistrationId}:{index}",
                        FromRegistrationId = site.FromRegistrationId,
                        DeclaredType = site.DeclaredType,
                        InjectionMechanism = site.Mechanism,
                        ParameterName = site.ParameterName,
                        CandidateRegistrationIds = conditionalOnly.Select(c => c.Id).ToList(),
                        ResolvedUnconditionalTargetId = target.Id
                    });
                }

                continue;
            }

            if (unconditional.Count == 0 && conditionalOnly.Count > 0)
            {
                conditional.Add(new ConditionalInjection
                {
                    Id = $"cond:{site.FromRegistrationId}:{index++}",
                    FromRegistrationId = site.FromRegistrationId,
                    DeclaredType = site.DeclaredType,
                    InjectionMechanism = site.Mechanism,
                    ParameterName = site.ParameterName,
                    CandidateRegistrationIds = conditionalOnly.Select(c => c.Id).ToList()
                });
                continue;
            }

            if (unconditional.Count > 1)
            {
                unresolved.Add(new UnresolvedInjection
                {
                    Id = $"unresolved:{site.FromRegistrationId}:{index++}",
                    FromRegistrationId = site.FromRegistrationId,
                    DeclaredType = site.DeclaredType,
                    InjectionMechanism = site.Mechanism,
                    ParameterName = site.ParameterName,
                    Reason = "ambiguous",
                    AmbiguousCandidateIds = unconditional.Select(c => c.Id).ToList()
                });
                continue;
            }

            unresolved.Add(new UnresolvedInjection
            {
                Id = $"unresolved:{site.FromRegistrationId}:{index++}",
                FromRegistrationId = site.FromRegistrationId,
                DeclaredType = site.DeclaredType,
                InjectionMechanism = site.Mechanism,
                ParameterName = site.ParameterName,
                Reason = "no_candidate"
            });
        }

        return (edges, conditional, unresolved);
    }

    private static ReachabilityState GetState(RegistrationNode node, string contextId) =>
        node.ContextMemberships.FirstOrDefault(m => m.ContextId == contextId)?.State ?? ReachabilityState.Unresolved;

    private static List<RegistrationNode> FindCandidates(List<RegistrationNode> nodes, InjectionSite site, string contextId)
    {
        var qualifier = site.Annotations.FirstOrDefault(a => a.Is("Qualifier") || a.Is("Named"));
        var qualifierValue = qualifier?.Arguments.TryGetValue("value", out var v) == true ? v.Trim('"') : null;

        var matches = nodes.Where(n =>
        {
            var m = n.ContextMemberships.FirstOrDefault(cm => cm.ContextId == contextId);
            if (m == null || m.State is ReachabilityState.External or ReachabilityState.Unresolved)
                return false;

            if (!TypeMatches(n, site.DeclaredType))
                return false;

            if (qualifierValue != null)
                return string.Equals(n.PrimaryBeanName, qualifierValue, StringComparison.Ordinal) ||
                       n.BeanAliases.Contains(qualifierValue, StringComparer.Ordinal) ||
                       n.QualifierConstraints.Any(q => q.Value == qualifierValue);

            return true;
        }).ToList();

        if (matches.Count > 1)
        {
            var primary = matches.Where(n => n.IsPrimary).ToList();
            if (primary.Count == 1)
                return primary;
        }

        return matches;
    }

    private static bool TypeMatches(RegistrationNode node, TypeRef declared)
    {
        if (node.ExposedType != null && TypesEqual(node.ExposedType, declared))
            return true;

        if (node.ImplementationType != null && TypesEqual(node.ImplementationType, declared))
            return true;

        return node.Aliases.Any(a => TypesEqual(a, declared));
    }

    private static bool TypesEqual(TypeRef a, TypeRef b)
    {
        if (!string.Equals(a.FullyQualifiedName, b.FullyQualifiedName, StringComparison.Ordinal))
            return false;

        if (a.IsGeneric && b.IsGeneric && a.TypeArguments.Count > 0 && b.TypeArguments.Count > 0)
            return a.TypeArguments.Zip(b.TypeArguments).All(pair => TypesEqual(pair.First, pair.Second));

        return true;
    }
}
