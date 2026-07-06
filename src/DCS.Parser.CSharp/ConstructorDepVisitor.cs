using DCS.Core.IR;
using DCS.Parser.CSharp.Semantic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

public sealed record ConstructorDependency(
    string ImplementationKey,
    ResolvedTypeIdentity? ImplementationIdentity,
    string ImplementationShortName,
    List<ResolvedParameterDependency> Parameters);

public sealed record ResolvedParameterDependency(
    ResolvedTypeIdentity? Identity,
    string SyntacticName,
    TypeResolutionQuality Quality);

internal sealed class ConstructorDepVisitor : CSharpSyntaxWalker
{
    private readonly SemanticModel? _model;
    private readonly string _projectScopeId;
    private readonly Dictionary<string, ConstructorDependency> _constructorDeps = [];

    public IReadOnlyDictionary<string, ConstructorDependency> ConstructorDeps => _constructorDeps;

    public ConstructorDepVisitor(SemanticModel? model = null, string? projectScopeId = null)
    {
        _model = model;
        _projectScopeId = projectScopeId ?? "syntactic";
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var className = node.Identifier.Text;
        var ctors = node.Members.OfType<ConstructorDeclarationSyntax>()
            .Where(IsDiActivatableConstructor)
            .ToList();
        var mainCtor = ctors
            .OrderByDescending(InjectableParameterCount)
            .ThenByDescending(c => c.ParameterList.Parameters.Count(p => p.Default == null))
            .FirstOrDefault();
        if (mainCtor == null)
        {
            base.VisitClassDeclaration(node);
            return;
        }

        ResolvedTypeIdentity? implIdentity = null;
        if (_model != null)
            implIdentity = TypeIdentityFormatter.Format(_model.GetDeclaredSymbol(node), _projectScopeId);

        var implKey = implIdentity?.CanonicalKey ?? className;

        var deps = mainCtor.ParameterList.Parameters
            .Where(p => p.Type != null && p.Default == null && IsInjectableParameterType(p.Type))
            .Select(p =>
            {
                var syntactic = GetTypeName(p.Type!);
                if (_model == null)
                    return new ResolvedParameterDependency(null, syntactic, TypeResolutionQuality.SyntacticFallback);

                var resolver = new SemanticTypeResolver(_model, _projectScopeId);
                var resolved = resolver.Resolve(p.Type!);
                return new ResolvedParameterDependency(
                    resolved.ServiceType.Resolved,
                    syntactic,
                    resolved.Quality);
            })
            .ToList();

        if (deps.Count > 0)
            _constructorDeps[implKey] = new ConstructorDependency(implKey, implIdentity, className, deps);

        base.VisitClassDeclaration(node);
    }

    /// <summary>
    /// MS DI only activates public constructors (same-assembly internal types may use internal ctors;
    /// we prefer public to avoid analyzing helper-only ctor chains with inline <c>new</c> deps).
    /// </summary>
    private static bool IsDiActivatableConstructor(ConstructorDeclarationSyntax ctor) =>
        !ctor.Modifiers.Any(m =>
            m.IsKind(SyntaxKind.PrivateKeyword) ||
            m.IsKind(SyntaxKind.ProtectedKeyword) ||
            m.IsKind(SyntaxKind.InternalKeyword));

    private static int InjectableParameterCount(ConstructorDeclarationSyntax ctor) =>
        ctor.ParameterList.Parameters.Count(p =>
            p.Type != null && p.Default == null && IsInjectableParameterType(p.Type));

    private static bool IsInjectableParameterType(TypeSyntax type)
    {
        var name = GetTypeName(type).TrimEnd('?');
        return name switch
        {
            "string" or "int" or "long" or "short" or "byte" or "bool" or "float" or "double" or "decimal" or "char" or "object" => false,
            _ when name.StartsWith("syntactic:", StringComparison.Ordinal) => false,
            _ => true
        };
    }

    private static string GetTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax qn => qn.ToString(),
        GenericNameSyntax gn =>
            $"{gn.Identifier.Text}<{string.Join(", ", gn.TypeArgumentList.Arguments.Select(GetTypeName))}>",
        PredefinedTypeSyntax pt => pt.Keyword.Text,
        NullableTypeSyntax nt => $"{GetTypeName(nt.ElementType)}?",
        _ => type.ToString()
    };
}
