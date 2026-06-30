using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp.Semantic;

public sealed class SemanticTypeResolver
{
    private readonly SemanticModel _model;
    private readonly string _projectScopeId;

    public SemanticTypeResolver(SemanticModel model, string projectScopeId)
    {
        _model = model;
        _projectScopeId = projectScopeId;
    }

    public TypeResolutionResult Resolve(TypeSyntax syntax)
    {
        var symbol = _model.GetTypeInfo(syntax).Type;
        if (symbol == null || symbol.Kind == SymbolKind.ErrorType)
        {
            var syntactic = GetSyntacticName(syntax);
            return new TypeResolutionResult
            {
                Quality = DCS.Core.IR.TypeResolutionQuality.SyntacticFallback,
                ServiceType = DCS.Core.IR.ServiceTypeIdentity.FromSyntactic(syntactic),
                TypeRef = TypeIdentityFormatter.SyntacticFallbackTypeRef(syntactic)
            };
        }

        var identity = TypeIdentityFormatter.Format(symbol, _projectScopeId);
        if (identity == null)
        {
            var syntactic = GetSyntacticName(syntax);
            return new TypeResolutionResult
            {
                Quality = DCS.Core.IR.TypeResolutionQuality.Error,
                ServiceType = DCS.Core.IR.ServiceTypeIdentity.FromSyntactic(syntactic),
                TypeRef = TypeIdentityFormatter.SyntacticFallbackTypeRef(syntactic)
            };
        }

        return new TypeResolutionResult
        {
            Quality = DCS.Core.IR.TypeResolutionQuality.Resolved,
            ServiceType = DCS.Core.IR.ServiceTypeIdentity.FromResolved(identity),
            TypeRef = TypeIdentityFormatter.ToTypeRef(identity)
        };
    }

    public TypeResolutionResult ResolveFromSymbol(ITypeSymbol? symbol)
    {
        if (symbol == null || symbol.Kind == SymbolKind.ErrorType)
        {
            return new TypeResolutionResult
            {
                Quality = DCS.Core.IR.TypeResolutionQuality.SyntacticFallback,
                ServiceType = DCS.Core.IR.ServiceTypeIdentity.FromSyntactic("unknown"),
                TypeRef = TypeIdentityFormatter.SyntacticFallbackTypeRef("unknown")
            };
        }

        var identity = TypeIdentityFormatter.Format(symbol, _projectScopeId);
        if (identity == null)
        {
            var syntactic = symbol.Name;
            return new TypeResolutionResult
            {
                Quality = DCS.Core.IR.TypeResolutionQuality.Error,
                ServiceType = DCS.Core.IR.ServiceTypeIdentity.FromSyntactic(syntactic),
                TypeRef = TypeIdentityFormatter.SyntacticFallbackTypeRef(syntactic)
            };
        }

        return new TypeResolutionResult
        {
            Quality = DCS.Core.IR.TypeResolutionQuality.Resolved,
            ServiceType = DCS.Core.IR.ServiceTypeIdentity.FromResolved(identity),
            TypeRef = TypeIdentityFormatter.ToTypeRef(identity)
        };
    }

    public DCS.Core.IR.ResolvedTypeIdentity? ResolveIdentity(TypeSyntax syntax) =>
        TypeIdentityFormatter.Format(_model.GetTypeInfo(syntax).Type, _projectScopeId);

    private static string GetSyntacticName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax qn => qn.ToString(),
        GenericNameSyntax gn =>
            $"{gn.Identifier.Text}<{string.Join(", ", gn.TypeArgumentList.Arguments.Select(GetSyntacticName))}>",
        PredefinedTypeSyntax pt => pt.Keyword.Text,
        NullableTypeSyntax nt => $"{GetSyntacticName(nt.ElementType)}?",
        ArrayTypeSyntax at => $"{GetSyntacticName(at.ElementType)}[]",
        _ => type.ToString()
    };
}

public sealed record TypeResolutionResult
{
    public required DCS.Core.IR.TypeResolutionQuality Quality { get; init; }
    public required DCS.Core.IR.ServiceTypeIdentity ServiceType { get; init; }
    public required DCS.Core.IR.TypeRef TypeRef { get; init; }
}
