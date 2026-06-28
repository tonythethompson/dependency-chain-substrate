namespace DCS.Core.IR;

public sealed record TypeRef
{
    public required string FullyQualifiedName { get; init; }
    public required string ShortName { get; init; }
    public string? Namespace { get; init; }
    public string? Assembly { get; init; }
    public string Language { get; init; } = "csharp";
    public bool IsGeneric { get; init; }
    public List<TypeRef> TypeArguments { get; init; } = [];

    public static TypeRef FromShortName(string shortName) => new()
    {
        FullyQualifiedName = shortName,
        ShortName = shortName
    };

    public static TypeRef FromQualifiedName(string fullyQualifiedName)
    {
        var lastDot = fullyQualifiedName.LastIndexOf('.');
        return new TypeRef
        {
            FullyQualifiedName = fullyQualifiedName,
            ShortName = lastDot >= 0 ? fullyQualifiedName[(lastDot + 1)..] : fullyQualifiedName,
            Namespace = lastDot >= 0 ? fullyQualifiedName[..lastDot] : null
        };
    }
}
