namespace DCS.Parser.CSharp.Semantic;

public static class TypeIdentityMatcher
{
    public static bool Matches(
        DCS.Core.IR.ResolvedTypeIdentity registrationType,
        DCS.Core.IR.ResolvedTypeIdentity dependencyType,
        bool openGenericRegistration)
    {
        if (openGenericRegistration)
            return MatchesOpenGeneric(registrationType, dependencyType);

        return registrationType.CanonicalKey.Equals(dependencyType.CanonicalKey, StringComparison.Ordinal);
    }

    private static bool MatchesOpenGeneric(
        DCS.Core.IR.ResolvedTypeIdentity registrationType,
        DCS.Core.IR.ResolvedTypeIdentity dependencyType)
    {
        if (registrationType.MetadataName.Contains('`') &&
            dependencyType.MetadataName.StartsWith(registrationType.MetadataName.Split('`')[0], StringComparison.Ordinal))
            return true;

        return registrationType.CanonicalKey.Equals(dependencyType.CanonicalKey, StringComparison.Ordinal);
    }
}
