using DCS.Analysis;
using DCS.Core.IR;
using DCS.Parser.CSharp.Semantic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

public sealed class RegistrationPatternVisitor : CSharpSyntaxWalker
{
    private readonly string _filePath;
    private readonly List<string> _usings;
    private readonly FrameworkBoundaryModel _boundaries;
    private readonly SemanticRegistrationContext? _semantic;
    private readonly List<RegistrationNode> _registrations = [];
    private readonly List<BlindSpotReport> _blindSpots = [];

    private static readonly HashSet<string> KnownRegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddSingleton", "AddScoped", "AddTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient",
        "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient",
        "TryAddKeyedSingleton", "TryAddKeyedScoped", "TryAddKeyedTransient",
    };

    private static readonly HashSet<string> AssemblyScanningMethods = new(StringComparer.Ordinal)
    {
        "Scan", "RegisterServicesFromAssembly", "RegisterServicesFromAssemblies",
        "AddServicesFromAssembly", "AddFromAssembly", "RegisterAll"
    };

    private static readonly HashSet<string> OptionsRegistrationMethods = new(StringComparer.Ordinal)
    {
        "Configure", "Bind", "AddOptions"
    };

    private static readonly HashSet<string> TypedHttpClientMethods = new(StringComparer.Ordinal)
    {
        "AddHttpClient"
    };

    private static readonly HashSet<string> WalkableCompositionExtensions = new(StringComparer.Ordinal)
    {
        "AddTrackdub", "AddHeadlessTrackdub", "AddAvaloniaPlayback", "AddLocalization",
        "AddBilling", "AddHostedService"
    };

    private static readonly HashSet<string> SuppressedExtensionBlindSpots = new(StringComparer.Ordinal)
    {
        "TryAddEnumerable", "AddOpenApi", "AddCors", "AddSwaggerGen", "AddControllers",
        "AddEndpointsApiExplorer", "AddMvc", "AddRazorPages", "AddHealthChecks",
        "AddAuthentication", "AddAuthorization",
    };

    public IReadOnlyList<RegistrationNode> Registrations => _registrations;
    public IReadOnlyList<BlindSpotReport> BlindSpots => _blindSpots;

    public RegistrationPatternVisitor(
        string filePath,
        List<string> usings,
        FrameworkBoundaryModel? boundaries = null,
        SemanticRegistrationContext? semantic = null)
    {
        _filePath = filePath;
        _usings = usings;
        _boundaries = boundaries ?? FrameworkBoundaryModel.Default;
        _semantic = semantic;
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax ma &&
            IsServiceCollectionReceiver(ma.Expression))
        {
            var methodName = ma.Name.Identifier.Text;

            if (AssemblyScanningMethods.Contains(methodName))
            {
                _blindSpots.Add(new BlindSpotReport
                {
                    Pattern = "assembly_scanning",
                    Location = LocationOf(node),
                    Description = $"{methodName}() — assembly-scanning registrations not statically resolvable"
                });
            }
            else if (KnownRegistrationMethods.Contains(methodName))
            {
                var reg = TryExtractRegistration(node, ma.Name, methodName);
                if (reg != null) _registrations.Add(reg);
            }
            else if (OptionsRegistrationMethods.Contains(methodName) &&
                     ma.Name is GenericNameSyntax { TypeArgumentList.Arguments: [var optionsType, ..] })
            {
                var reg = MakeOptionsConfigurationNode(optionsType, location: LocationOf(node), node, methodName);
                if (reg != null) _registrations.Add(reg);
            }
            else if (TypedHttpClientMethods.Contains(methodName))
            {
                var reg = TryExtractTypedHttpClientRegistration(node, ma.Name, methodName);
                if (reg != null) _registrations.Add(reg);
            }
            else if (WalkableCompositionExtensions.Contains(methodName))
            {
                InlineCompositionExtensionRegistrations(node, methodName);
            }
            else if (methodName.StartsWith("Add", StringComparison.Ordinal) ||
                     methodName.StartsWith("TryAdd", StringComparison.Ordinal))
            {
                if (!SuppressedExtensionBlindSpots.Contains(methodName))
                {
                    _blindSpots.Add(new BlindSpotReport
                    {
                        Pattern = "extension_method",
                        Location = LocationOf(node),
                        Description = $"{methodName}() — internal registrations not traced"
                    });
                }
            }
        }

        base.VisitInvocationExpression(node);
    }

    private RegistrationNode? TryExtractRegistration(
        InvocationExpressionSyntax node, SimpleNameSyntax nameNode, string methodName)
    {
        var lifetime = LifetimeFromMethodName(methodName);
        var isKeyed = methodName.Contains("Keyed", StringComparison.Ordinal);
        var isTryAdd = methodName.StartsWith("TryAdd", StringComparison.Ordinal);
        var location = LocationOf(node);
        var callArgs = node.ArgumentList.Arguments.ToList();
        var dataArgs = isKeyed ? callArgs.Skip(1).ToList() : callArgs;

        var recognition = _semantic?.Model != null
            ? RegistrationApiVerifier.Verify(node, _semantic.Model, methodName)
            : RegistrationRecognitionQuality.SyntaxCandidateUnverified;

        if (nameNode is GenericNameSyntax { TypeArgumentList.Arguments: var typeArgs })
        {
            var types = typeArgs.ToList();
            if (types.Count >= 2)
            {
                var firstDataArg = dataArgs.FirstOrDefault();
                if (firstDataArg?.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                {
                    var shallowType = ExtractCreatedTypeFromLambdaExpression(firstDataArg.Expression);
                    if (shallowType != null)
                    {
                        return firstDataArg.Expression switch
                        {
                            LambdaExpressionSyntax lambdaExpr => MakeShallowFactoryNode(
                                types[0], shallowType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, lambda: lambdaExpr),
                            AnonymousMethodExpressionSyntax anonExpr => MakeShallowFactoryNode(
                                types[0], shallowType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, anonLambda: anonExpr),
                            _ => MakeShallowFactoryNode(types[0], shallowType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition)
                        };
                    }
                    return MakeFactoryLambdaBlindSpotNode(
                        types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition,
                        firstDataArg.Expression);
                }
                return MakeExplicitNode(types[0], types[1], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition);
            }

            if (types.Count == 1)
            {
                var firstDataArg = dataArgs.FirstOrDefault();
                return firstDataArg?.Expression switch
                {
                    LambdaExpressionSyntax lambdaExpr =>
                        ExtractCreatedTypeFromLambdaExpression(lambdaExpr) is { } shallowType
                            ? MakeShallowFactoryNode(types[0], shallowType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, lambda: lambdaExpr)
                            : MakeFactoryLambdaBlindSpotNode(types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, lambdaExpr),
                    AnonymousMethodExpressionSyntax anonExpr =>
                        ExtractCreatedTypeFromLambdaExpression(anonExpr) is { } shallowAnonType
                            ? MakeShallowFactoryNode(types[0], shallowAnonType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, anonLambda: anonExpr)
                            : MakeFactoryLambdaBlindSpotNode(types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, anonExpr),
                    ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax =>
                        MakeDegradedNode(types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, "instance_arg"),
                    null =>
                        MakeExplicitNode(types[0], types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition),
                    _ =>
                        MakeDegradedNode(types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, "unknown_arg")
                };
            }
        }

        var typeOfArgs = callArgs
            .Select(a => a.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .Select(t => t.Type)
            .ToList();

        if (typeOfArgs.Count >= 2)
            return MakeExplicitNode(typeOfArgs[0], typeOfArgs[1], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition);
        if (typeOfArgs.Count == 1)
            return MakeExplicitNode(typeOfArgs[0], typeOfArgs[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition);

        if (dataArgs.Count == 1)
        {
            var argExpr = dataArgs[0].Expression;
            if (argExpr is LambdaExpressionSyntax lambda)
            {
                var shallowType = ShallowFactoryLambdaExtractor.TryExtractCreatedType(lambda);
                if (shallowType != null)
                {
                    return MakeShallowFactoryNode(
                        shallowType, shallowType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, lambda);
                }
            }
            else if (argExpr is AnonymousMethodExpressionSyntax anonLambda)
            {
                var shallowType = ShallowFactoryLambdaExtractor.TryExtractCreatedType(anonLambda);
                if (shallowType != null)
                {
                    return MakeShallowFactoryNode(
                        shallowType, shallowType, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, anonLambda: anonLambda);
                }
            }
            else if (argExpr is not LambdaExpressionSyntax and not AnonymousMethodExpressionSyntax)
            {
                var instanceNode = TryExtractInstanceRegistration(
                    argExpr, lifetime, isKeyed, isTryAdd, location, node, methodName, recognition);
                if (instanceNode != null)
                    return instanceNode;
            }
        }

        _blindSpots.Add(new BlindSpotReport
        {
            Pattern = "unrecognized_pattern",
            Location = location,
            Description = $"{methodName}() — argument pattern not recognised"
        });
        return null;
    }

    private TypeSyntax? ExtractCreatedTypeFromLambdaExpression(ExpressionSyntax expression) =>
        expression switch
        {
            LambdaExpressionSyntax lambda => ShallowFactoryLambdaExtractor.TryExtractCreatedType(lambda) ??
                                             TryExtractImplicitNewType(lambda),
            AnonymousMethodExpressionSyntax anon => ShallowFactoryLambdaExtractor.TryExtractCreatedType(anon),
            _ => null
        };

    private TypeSyntax? TryExtractImplicitNewType(LambdaExpressionSyntax lambda)
    {
        if (_semantic?.Model == null || !ShallowFactoryLambdaExtractor.ContainsImplicitObjectCreation(lambda))
            return null;

        var converted = _semantic.Model.GetTypeInfo(lambda).ConvertedType;
        if (converted == null || converted.SpecialType == SpecialType.System_Void)
            return null;

        var display = converted.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return SyntaxFactory.ParseTypeName(display);
    }

    private RegistrationNode? MakeOptionsConfigurationNode(
        TypeSyntax optionsType,
        SourceRef location,
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        var resolved = ResolveType(optionsType);
        var recognition = _semantic?.Model != null
            ? RegistrationApiVerifier.Verify(invocation, _semantic.Model, methodName)
            : RegistrationRecognitionQuality.SyntaxCandidateUnverified;

        var node = BuildNode(
            resolved, resolved, Lifetime.Singleton, isKeyed: false, isTryAdd: false,
            location, invocation, methodName, recognition, Confidence.Explicit, "options_configuration");
        node.Annotations["options_type"] = resolved.TypeRef.ShortName;
        return node;
    }

    private RegistrationNode? TryExtractTypedHttpClientRegistration(
        InvocationExpressionSyntax node,
        SimpleNameSyntax nameNode,
        string methodName)
    {
        if (nameNode is not GenericNameSyntax { TypeArgumentList.Arguments: var typeArgs } ||
            typeArgs.Count == 0)
        {
            _blindSpots.Add(new BlindSpotReport
            {
                Pattern = "extension_method",
                Location = LocationOf(node),
                Description = "AddHttpClient() — untyped client registration not traced"
            });
            return null;
        }

        var location = LocationOf(node);
        var recognition = _semantic?.Model != null
            ? RegistrationApiVerifier.Verify(node, _semantic.Model, methodName)
            : RegistrationRecognitionQuality.SyntaxCandidateUnverified;

        if (typeArgs.Count >= 2)
        {
            return MakeExplicitNode(
                typeArgs[0], typeArgs[1], Lifetime.Transient, isKeyed: false, isTryAdd: false,
                location, node, methodName, recognition);
        }

        var clientType = typeArgs[0];
        return MakeExplicitNode(
            clientType, clientType, Lifetime.Transient, isKeyed: false, isTryAdd: false,
            location, node, methodName, recognition);
    }

    private void InlineCompositionExtensionRegistrations(InvocationExpressionSyntax node, string methodName)
    {
        if (_semantic?.Model == null)
            return;

        var symbol = _semantic.Model.GetSymbolInfo(node).Symbol as IMethodSymbol;
        if (symbol == null)
            return;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax is not MethodDeclarationSyntax methodDecl)
                continue;

            var calleePath = syntaxRef.SyntaxTree.FilePath.Replace('\\', '/');
            var calleeVisitor = new RegistrationPatternVisitor(calleePath, _usings, _boundaries, _semantic);
            if (methodDecl.Body != null)
                calleeVisitor.Visit(methodDecl.Body);
            else if (methodDecl.ExpressionBody != null)
                calleeVisitor.Visit(methodDecl.ExpressionBody);

            _registrations.AddRange(calleeVisitor.Registrations);
            _blindSpots.AddRange(calleeVisitor.BlindSpots);
        }
    }

    private RegistrationNode? TryExtractInstanceRegistration(
        ExpressionSyntax expression,
        Lifetime lifetime, bool isKeyed, bool isTryAdd, SourceRef location,
        InvocationExpressionSyntax invocation, string methodName,
        RegistrationRecognitionQuality recognition)
    {
        if (_semantic?.Model == null || _semantic.Resolver == null)
            return null;

        var symbol = _semantic.Model.GetTypeInfo(expression).Type;
        if (symbol == null || symbol.SpecialType == SpecialType.System_Void)
            return null;

        var abstractResolved = _semantic.Resolver.ResolveFromSymbol(symbol);
        return BuildNode(abstractResolved, null, lifetime, isKeyed, isTryAdd, location,
            invocation, methodName, recognition, Confidence.Degraded, "instance");
    }

    private RegistrationNode MakeShallowFactoryNode(
        TypeSyntax abstractType, TypeSyntax concreteType,
        Lifetime lifetime, bool isKeyed, bool isTryAdd, SourceRef location,
        InvocationExpressionSyntax invocation, string methodName,
        RegistrationRecognitionQuality recognition,
        LambdaExpressionSyntax? lambda = null,
        AnonymousMethodExpressionSyntax? anonLambda = null)
    {
        var abstractResolved = ResolveType(abstractType);
        var concreteResolved = ResolveType(concreteType);
        _blindSpots.Add(new BlindSpotReport
        {
            Pattern = "factory_lambda_shallow",
            Location = location,
            Description = $"{abstractResolved.TypeRef.ShortName} — shallow factory lambda (dependencies partially traced)"
        });
        var node = BuildNode(abstractResolved, concreteResolved, lifetime, isKeyed, isTryAdd, location,
            invocation, methodName, recognition, Confidence.BlindSpot, "factory_lambda_shallow");

        var serviceRequests = lambda != null
            ? ShallowFactoryLambdaExtractor.TryExtractServiceRequestTypes(lambda)
            : anonLambda != null
                ? ShallowFactoryLambdaExtractor.TryExtractServiceRequestTypes(anonLambda)
                : [];

        var serviceKeys = serviceRequests
            .Select(t => ResolveType(t).ServiceType.CanonicalKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (serviceKeys.Count > 0)
            node.Annotations["factory_lambda_service_keys"] = string.Join(";", serviceKeys);

        if (serviceKeys.Count == 0)
            return node with { ParserConfidence = Confidence.Degraded };

        return node;
    }

    private RegistrationNode MakeExplicitNode(
        TypeSyntax abstractType, TypeSyntax concreteType,
        Lifetime lifetime, bool isKeyed, bool isTryAdd, SourceRef location,
        InvocationExpressionSyntax invocation, string methodName,
        RegistrationRecognitionQuality recognition)
    {
        var abstractResolved = ResolveType(abstractType);
        var concreteResolved = ResolveType(concreteType);
        return BuildNode(abstractResolved, concreteResolved, lifetime, isKeyed, isTryAdd, location,
            invocation, methodName, recognition, Confidence.Explicit, null);
    }

    private RegistrationNode MakeDegradedNode(
        TypeSyntax abstractType, Lifetime lifetime,
        bool isKeyed, bool isTryAdd, SourceRef location,
        InvocationExpressionSyntax invocation, string methodName,
        RegistrationRecognitionQuality recognition, string reason) =>
        BuildNode(ResolveType(abstractType), null, lifetime, isKeyed, isTryAdd, location,
            invocation, methodName, recognition, Confidence.Degraded, reason);

    private RegistrationNode MakeBlindSpotNode(
        TypeSyntax abstractType, Lifetime lifetime,
        bool isKeyed, bool isTryAdd, SourceRef location,
        InvocationExpressionSyntax invocation, string methodName,
        RegistrationRecognitionQuality recognition, string reason)
    {
        var abstractResolved = ResolveType(abstractType);
        _blindSpots.Add(new BlindSpotReport
        {
            Pattern = reason,
            Location = location,
            Description = $"{abstractResolved.TypeRef.ShortName} — registered via {reason}, dependencies not resolvable"
        });
        return BuildNode(abstractResolved, null, lifetime, isKeyed, isTryAdd, location,
            invocation, methodName, recognition, Confidence.BlindSpot, reason);
    }

    private RegistrationNode MakeFactoryLambdaBlindSpotNode(
        TypeSyntax abstractType,
        Lifetime lifetime,
        bool isKeyed,
        bool isTryAdd,
        SourceRef location,
        InvocationExpressionSyntax invocation,
        string methodName,
        RegistrationRecognitionQuality recognition,
        ExpressionSyntax lambdaExpression)
    {
        var node = MakeBlindSpotNode(
            abstractType, lifetime, isKeyed, isTryAdd, location, invocation, methodName, recognition, "factory_lambda");
        return AnnotateFactoryServiceKeys(node, lambdaExpression);
    }

    private RegistrationNode AnnotateFactoryServiceKeys(RegistrationNode node, ExpressionSyntax lambdaExpression)
    {
        IReadOnlyList<TypeSyntax> serviceRequests = lambdaExpression switch
        {
            LambdaExpressionSyntax lambda => ShallowFactoryLambdaExtractor.TryExtractServiceRequestTypes(lambda),
            AnonymousMethodExpressionSyntax anon => ShallowFactoryLambdaExtractor.TryExtractServiceRequestTypes(anon),
            _ => Array.Empty<TypeSyntax>()
        };

        if (serviceRequests.Count == 0)
            return node;

        var serviceKeys = serviceRequests
            .Select(t => ResolveType(t).ServiceType.CanonicalKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (serviceKeys.Count == 0)
            return node;

        var annotations = new Dictionary<string, string>(node.Annotations, StringComparer.Ordinal)
        {
            ["factory_lambda_service_keys"] = string.Join(";", serviceKeys)
        };
        return node with { Annotations = annotations };
    }

    private RegistrationNode BuildNode(
        TypeResolutionResult abstractResolved,
        TypeResolutionResult? concreteResolved,
        Lifetime lifetime, bool isKeyed, bool isTryAdd, SourceRef location,
        InvocationExpressionSyntax invocation, string methodName,
        RegistrationRecognitionQuality recognition, Confidence confidence, string? reason)
    {
        var span = invocation.GetLocation().GetLineSpan();
        var ordinal = _semantic?.RegistrationOrdinal ?? 0;
        if (_semantic != null) _semantic.RegistrationOrdinal++;

        var scopeId = _semantic?.Scope.ScopeId ?? "syntactic";
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId(
            scopeId, location.FilePath,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
            ordinal);

        var duplicateScopeId = _semantic?.Scope != null
            ? ShellCompositionScope.RuntimeScopeForDuplicate(_semantic.Scope, _filePath)
            : _semantic?.Scope.CompositionScopeId ?? scopeId;

        var duplicateGroupKey = RegistrationNode.ComputeDuplicateGroupKey(
            duplicateScopeId,
            abstractResolved.ServiceType);

        var fingerprint = RegistrationStatementFingerprint.Compute(
            methodName, lifetime, abstractResolved.TypeRef.ShortName);

        var annotations = BuildAnnotations(isKeyed, isTryAdd, reason);
        ConditionalRegistrationDetector.ApplyIfElseAnnotation(invocation, annotations);
        if (_semantic?.Scope.ProjectEvaluationIncomplete == true)
            annotations["project_evaluation_incomplete"] = "true";
        if (_semantic?.Scope.ImplicitUsingsUnmodeled == true)
            annotations["implicit_usings_unmodeled"] = "true";
        if (abstractResolved.Quality == TypeResolutionQuality.SyntacticFallback)
            annotations["type_identity_quality"] = "syntactic_fallback";
        if (recognition == RegistrationRecognitionQuality.SyntaxCandidateUnverified)
            annotations["registration_api"] = "unverified";

        var node = new RegistrationNode
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            ServiceType = abstractResolved.ServiceType,
            DuplicateGroupKey = duplicateGroupKey,
            CompositionScopeId = _semantic?.Scope.CompositionScopeId ?? scopeId,
            TypeResolutionQuality = abstractResolved.Quality,
            RegistrationRecognitionQuality = recognition,
            RegistrationStatementFingerprint = fingerprint,
            DisplayName = abstractResolved.TypeRef.ShortName,
            AbstractToken = abstractResolved.TypeRef,
            ConcreteImpl = concreteResolved != null &&
                           concreteResolved.TypeRef.ShortName != abstractResolved.TypeRef.ShortName
                ? concreteResolved.TypeRef
                : null,
            Lifetime = lifetime,
            SourceLocation = location,
            ParserConfidence = confidence,
            FrameworkTags = InferFrameworkTags(abstractResolved.TypeRef),
            Annotations = annotations
        };

        if (StrictDuplicateEligibility.IsEligible(node))
            node.Annotations[StrictDuplicateEligibility.AnnotationKey] = "true";

        return node;
    }

    private TypeResolutionResult ResolveType(TypeSyntax type)
    {
        if (_semantic?.Resolver != null)
            return _semantic.Resolver.Resolve(type);
        return SyntacticResolve(type);
    }

    private static TypeResolutionResult SyntacticResolve(TypeSyntax type)
    {
        var name = GetTypeName(type);
        var isGeneric = name.Contains('<');
        var baseName = isGeneric ? name[..name.IndexOf('<')] : name;
        var typeRef = new TypeRef
        {
            FullyQualifiedName = string.Empty,
            ShortName = baseName,
            IsGeneric = isGeneric
        };
        return new TypeResolutionResult
        {
            Quality = TypeResolutionQuality.SyntacticFallback,
            ServiceType = ServiceTypeIdentity.FromSyntactic(baseName),
            TypeRef = typeRef
        };
    }

    private List<string> InferFrameworkTags(TypeRef typeRef) =>
        FrameworkTagger.InferTags(_boundaries, _usings, typeRef, _filePath);

    private static string GetTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax qn => qn.ToString(),
        GenericNameSyntax gn =>
            $"{gn.Identifier.Text}<{string.Join(", ", gn.TypeArgumentList.Arguments.Select(GetTypeName))}>",
        PredefinedTypeSyntax pt => pt.Keyword.Text,
        NullableTypeSyntax nt => $"{GetTypeName(nt.ElementType)}?",
        ArrayTypeSyntax at => $"{GetTypeName(at.ElementType)}[]",
        _ => type.ToString()
    };

    private static bool IsServiceCollectionReceiver(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => IsServiceCollectionName(id.Identifier.Text),
        MemberAccessExpressionSyntax ma => IsServiceCollectionName(ma.Name.Identifier.Text),
        _ => false
    };

    private static bool IsServiceCollectionName(string name) =>
        string.Equals(name, "services", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "serviceCollection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "sc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Services", StringComparison.Ordinal) ||
        string.Equals(name, "container", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "serviceDescriptors", StringComparison.OrdinalIgnoreCase);

    private static Lifetime LifetimeFromMethodName(string name)
    {
        if (name.Contains("Singleton", StringComparison.Ordinal)) return Lifetime.Singleton;
        if (name.Contains("Scoped", StringComparison.Ordinal)) return Lifetime.Scoped;
        if (name.Contains("Transient", StringComparison.Ordinal)) return Lifetime.Transient;
        return Lifetime.Unknown;
    }

    private static Dictionary<string, string> BuildAnnotations(bool isKeyed, bool isTryAdd, string? reason)
    {
        var ann = new Dictionary<string, string>();
        if (isKeyed) ann["keyed"] = "true";
        if (isTryAdd) ann["conditional"] = "try_add";
        if (reason != null) ann["pattern"] = reason;
        return ann;
    }

    private SourceRef LocationOf(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return new SourceRef
        {
            FilePath = _filePath,
            Line = span.StartLinePosition.Line + 1,
            Column = span.StartLinePosition.Character + 1
        };
    }
}
