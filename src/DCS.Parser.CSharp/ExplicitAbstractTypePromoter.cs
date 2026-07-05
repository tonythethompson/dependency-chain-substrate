using DCS.Core.IR;
using DCS.Parser.CSharp.Semantic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

/// <summary>
/// Promotes syntactic abstract service types on explicit <c>Add*&lt;IAbstract, Impl&gt;</c> pairs
/// by resolving the abstract token against the scope compilation and validating the impl pairing.
/// </summary>
internal static class ExplicitAbstractTypePromoter
{
    public static List<RegistrationNode> Promote(
        List<RegistrationNode> nodes,
        IReadOnlyDictionary<string, ConstructorDependency> ctorDeps,
        IReadOnlyDictionary<string, ScopeCompilationResult> scopeCompilations)
    {
        var depsByShort = ctorDeps.Values
            .GroupBy(d => d.ImplementationShortName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        return nodes
            .Select(node => PromoteNode(node, depsByShort, scopeCompilations))
            .ToList();
    }

    private static RegistrationNode PromoteNode(
        RegistrationNode node,
        IReadOnlyDictionary<string, List<ConstructorDependency>> depsByShort,
        IReadOnlyDictionary<string, ScopeCompilationResult> scopeCompilations)
    {
        if (node.ParserConfidence != Confidence.Explicit)
            return node;
        if (node.TypeResolutionQuality == TypeResolutionQuality.Resolved)
            return node;
        if (node.ConcreteImpl == null)
            return node;
        if (!scopeCompilations.TryGetValue(node.CompositionScopeId, out var scopeResult))
            return node;

        var abstractShort = node.ServiceType?.SyntacticDisplay ?? node.DisplayName;
        if (string.IsNullOrWhiteSpace(abstractShort))
            return node;

        depsByShort.TryGetValue(node.ConcreteImpl.ShortName, out var depList);
        depList ??= [];
        var dep = depList.Count == 1
            ? depList[0]
            : depList.FirstOrDefault(d =>
                  d.ImplementationIdentity != null &&
                  MatchesConcreteImplementation(d.ImplementationIdentity, node.ConcreteImpl));
        dep ??= depList.FirstOrDefault();

        var implSymbol = ResolveImplementationSymbol(scopeResult, dep, node.ConcreteImpl, node.CompositionScopeId);
        var abstractSymbol = ResolveAbstractSymbol(scopeCompilations, abstractShort, implSymbol);
        if (abstractSymbol == null)
            return node;

        var identity = TypeIdentityFormatter.Format(abstractSymbol, node.CompositionScopeId);
        if (identity == null)
            return node;

        return ApplyPromotion(node, identity);
    }

    private static INamedTypeSymbol? ResolveAbstractSymbol(
        IReadOnlyDictionary<string, ScopeCompilationResult> scopeCompilations,
        string abstractShort,
        INamedTypeSymbol? implSymbol)
    {
        var abstractBase = abstractShort.Contains('<')
            ? abstractShort[..abstractShort.IndexOf('<')]
            : abstractShort.TrimEnd('?');

        foreach (var scopeResult in scopeCompilations.Values)
        {
            foreach (var metadataName in BuildAbstractMetadataNameCandidates(abstractShort, abstractBase))
            {
                if (scopeResult.Compilation.GetTypeByMetadataName(metadataName) is not INamedTypeSymbol byMetadata)
                    continue;
                if (implSymbol != null &&
                    !implSymbol.AllInterfaces.Contains(byMetadata, SymbolEqualityComparer.Default))
                    continue;
                return byMetadata;
            }

            var candidates = scopeResult.Compilation
                .GetSymbolsWithName(abstractBase, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .Where(s => s.TypeKind == TypeKind.Interface)
                .ToList();

            if (implSymbol != null)
            {
                var implemented = candidates
                    .Where(i => implSymbol.AllInterfaces.Contains(i, SymbolEqualityComparer.Default))
                    .ToList();
                if (implemented.Count > 0)
                    return implemented.FirstOrDefault(i =>
                        string.Equals(SymbolDisplayForMatch(i), abstractShort, StringComparison.Ordinal)) ?? implemented[0];
            }

            if (candidates.Count == 1)
                return candidates[0];
        }

        return null;
    }

    private static IEnumerable<string> BuildAbstractMetadataNameCandidates(string abstractShort, string abstractBase)
    {
        if (abstractShort.Contains('.'))
            yield return abstractShort;

        yield return $"Trackdub.Contracts.Diagnostics.{abstractBase}";
        yield return $"Trackdub.Contracts.{abstractBase}";
        yield return $"Trackdub.Contracts.ApplicationContracts.{abstractBase}";
    }

    private static RegistrationNode ApplyPromotion(RegistrationNode node, ResolvedTypeIdentity identity)
    {
        var typeRef = TypeIdentityFormatter.ToTypeRef(identity);
        var serviceType = ServiceTypeIdentity.FromResolved(identity);
        var annotations = new Dictionary<string, string>(node.Annotations);
        annotations.Remove("type_identity_quality");

        return node with
        {
            TypeResolutionQuality = TypeResolutionQuality.Resolved,
            ServiceType = serviceType,
            AbstractToken = typeRef,
            DisplayName = typeRef.ShortName,
            DuplicateGroupKey = RegistrationNode.ComputeDuplicateGroupKey(
                node.CompositionScopeId,
                serviceType),
            Annotations = annotations
        };
    }

    private static INamedTypeSymbol? ResolveImplementationSymbol(
        ScopeCompilationResult scopeResult,
        ConstructorDependency? dep,
        TypeRef concrete,
        string projectScopeId)
    {
        var fromSyntax = FindTypeSymbolInScopeCompilation(
            scopeResult,
            concrete.ShortName,
            dep?.ImplementationIdentity?.MetadataName,
            projectScopeId);
        if (fromSyntax != null)
            return fromSyntax;

        if (dep?.ImplementationIdentity != null)
        {
            var byMetadata = scopeResult.Compilation.GetTypeByMetadataName(dep.ImplementationIdentity.MetadataName);
            if (byMetadata is INamedTypeSymbol namedByMetadata)
                return namedByMetadata;
        }

        var candidates = scopeResult.Compilation
            .GetSymbolsWithName(concrete.ShortName, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .ToList();

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => candidates.FirstOrDefault(c =>
                     dep?.ImplementationIdentity != null &&
                     string.Equals(
                         TypeIdentityFormatter.Format(c, projectScopeId)?.MetadataName,
                         dep.ImplementationIdentity.MetadataName,
                         StringComparison.Ordinal)) ?? candidates[0]
        };
    }

    private static INamedTypeSymbol? FindTypeSymbolInScopeCompilation(
        ScopeCompilationResult scopeResult,
        string shortName,
        string? preferredMetadataName,
        string projectScopeId)
    {
        foreach (var tree in scopeResult.Compilation.SyntaxTrees)
        {
            var model = scopeResult.Compilation.GetSemanticModel(tree);

            foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (!string.Equals(typeDecl.Identifier.Text, shortName, StringComparison.Ordinal))
                    continue;

                if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
                    continue;

                if (preferredMetadataName != null)
                {
                    var metadataName = TypeIdentityFormatter.Format(symbol, projectScopeId)?.MetadataName;
                    if (!string.Equals(metadataName, preferredMetadataName, StringComparison.Ordinal))
                        continue;
                }

                return symbol;
            }
        }

        return null;
    }

    private static string SymbolDisplayForMatch(ITypeSymbol symbol)
    {
        if (symbol is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } named)
        {
            var args = string.Join(", ", named.TypeArguments.Select(a => a.Name));
            return $"{named.Name}<{args}>";
        }

        return symbol.Name;
    }

    private static bool MatchesConcreteImplementation(ResolvedTypeIdentity identity, TypeRef concrete)
    {
        if (string.Equals(identity.MetadataName, concrete.FullyQualifiedName, StringComparison.Ordinal))
            return true;

        if (string.Equals(identity.MetadataName, concrete.ShortName, StringComparison.Ordinal))
            return true;

        return identity.MetadataName.EndsWith(
            $".{concrete.ShortName}",
            StringComparison.Ordinal);
    }
}
