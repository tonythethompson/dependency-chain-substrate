using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp.Semantic;

public static class TypeIdentityFormatter
{
    public static DCS.Core.IR.ResolvedTypeIdentity? Format(ITypeSymbol? symbol, string? projectScopeId)
    {
        if (symbol == null || symbol.Kind == SymbolKind.ErrorType)
            return null;

        symbol = symbol.WithNullableAnnotation(NullableAnnotation.None);

        if (symbol.TypeKind == TypeKind.Dynamic)
            symbol = symbol.ContainingAssembly.GetTypeByMetadataName("System.Object") ?? symbol;

        return FormatCore(symbol, projectScopeId);
    }

    private static DCS.Core.IR.ResolvedTypeIdentity FormatCore(ITypeSymbol symbol, string? projectScopeId)
    {
        var assemblyKey = symbol.Locations.FirstOrDefault()?.Kind == LocationKind.MetadataFile
            ? DCS.Core.IR.AssemblyKey.FromMetadata(symbol.ContainingAssembly?.Name ?? "unknown")
            : DCS.Core.IR.AssemblyKey.FromProjectScope(projectScopeId ?? symbol.ContainingAssembly?.Name ?? "unknown");

        var metadataName = BuildMetadataName(symbol);

        var typeArgs = symbol switch
        {
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } named =>
                named.TypeArguments.Select(a => FormatCore(a, projectScopeId)).ToList(),
            IArrayTypeSymbol array =>
                [FormatCore(array.ElementType, projectScopeId)],
            _ => new List<DCS.Core.IR.ResolvedTypeIdentity>()
        };

        return new DCS.Core.IR.ResolvedTypeIdentity
        {
            AssemblyKey = assemblyKey,
            MetadataName = metadataName,
            TypeArguments = typeArgs
        };
    }

    private static string BuildMetadataName(ITypeSymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol { IsGenericType: true, TypeParameters.Length: > 0 } named =>
                $"{GetNamedTypeMetadataName(named.OriginalDefinition)}{FormatArity(named)}",
            INamedTypeSymbol named => GetNamedTypeMetadataName(named),
            IArrayTypeSymbol array => $"{BuildMetadataName(array.ElementType)}[{new string(',', array.Rank - 1)}]",
            IPointerTypeSymbol ptr => $"{BuildMetadataName(ptr.PointedAtType)}*",
            ITypeParameterSymbol tp => $"!{tp.Ordinal}",
            _ => symbol.MetadataName
        };
    }

    private static string GetNamedTypeMetadataName(INamedTypeSymbol named)
    {
        if (named.ContainingType != null)
            return $"{GetNamedTypeMetadataName(named.ContainingType)}+{named.MetadataName}";

        var ns = named.ContainingNamespace;
        if (ns == null || ns.IsGlobalNamespace)
            return named.MetadataName;

        return $"{ns.ToDisplayString()}.{named.MetadataName}";
    }

    private static string FormatArity(INamedTypeSymbol named)
    {
        if (named.TypeArguments.Length == 0) return string.Empty;
        var args = string.Join(",", named.TypeArguments.Select(a => BuildMetadataName(a)));
        return $"<{args}>";
    }

    public static DCS.Core.IR.TypeRef ToTypeRef(DCS.Core.IR.ResolvedTypeIdentity identity)
    {
        var display = identity.MetadataName;
        var plusIdx = display.LastIndexOf('+');
        var shortPart = plusIdx >= 0 ? display[(plusIdx + 1)..] : display;
        if (plusIdx < 0)
        {
            var lastDot = display.LastIndexOf('.');
            if (lastDot >= 0)
                shortPart = display[(lastDot + 1)..];
        }

        var tickIdx = shortPart.IndexOf('`');
        if (tickIdx >= 0) shortPart = shortPart[..tickIdx];
        var ltIdx = shortPart.IndexOf('<');
        if (ltIdx >= 0) shortPart = shortPart[..ltIdx];

        string? ns = null;
        if (plusIdx < 0)
        {
            var nsEnd = display.Length - shortPart.Length - 1;
            if (nsEnd > 0 && display[nsEnd] == '.')
                ns = display[..nsEnd];
        }

        return new DCS.Core.IR.TypeRef
        {
            FullyQualifiedName = display,
            ShortName = shortPart,
            Namespace = ns,
            Assembly = identity.AssemblyKey.Canonical,
            IsGeneric = identity.TypeArguments.Count > 0
        };
    }

    public static DCS.Core.IR.TypeRef SyntacticFallbackTypeRef(string syntacticDisplay) => new()
    {
        FullyQualifiedName = string.Empty,
        ShortName = syntacticDisplay,
        IsGeneric = syntacticDisplay.Contains('<')
    };
}
