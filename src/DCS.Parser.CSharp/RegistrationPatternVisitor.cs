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
            else if (methodName.StartsWith("Add", StringComparison.Ordinal) ||
                     methodName.StartsWith("TryAdd", StringComparison.Ordinal))
            {
                _blindSpots.Add(new BlindSpotReport
                {
                    Pattern = "extension_method",
                    Location = LocationOf(node),
                    Description = $"{methodName}() — internal registrations not traced"
                });
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
                    return MakeBlindSpotNode(types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, "factory_lambda");
                return MakeExplicitNode(types[0], types[1], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition);
            }

            if (types.Count == 1)
            {
                var firstDataArg = dataArgs.FirstOrDefault();
                return firstDataArg?.Expression switch
                {
                    LambdaExpressionSyntax or AnonymousMethodExpressionSyntax =>
                        MakeBlindSpotNode(types[0], lifetime, isKeyed, isTryAdd, location, node, methodName, recognition, "factory_lambda"),
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

        _blindSpots.Add(new BlindSpotReport
        {
            Pattern = "unrecognized_pattern",
            Location = location,
            Description = $"{methodName}() — argument pattern not recognised"
        });
        return null;
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

        var duplicateGroupKey = RegistrationNode.ComputeDuplicateGroupKey(
            _semantic?.Scope.CompositionScopeId ?? scopeId,
            abstractResolved.ServiceType);

        var fingerprint = RegistrationStatementFingerprint.Compute(
            methodName, lifetime, abstractResolved.TypeRef.ShortName);

        var annotations = BuildAnnotations(isKeyed, isTryAdd, reason);
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
        FrameworkTagger.InferTags(_boundaries, _usings, typeRef);

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
