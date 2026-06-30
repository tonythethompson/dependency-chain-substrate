namespace DCS.Parser.CSharp.Semantic;

/// <summary>
/// DI services commonly supplied by the host/framework rather than app registrations.
/// </summary>
public static class FrameworkProvidedServices
{
    private static readonly string[] SyntacticPrefixes =
    [
        "ILogger",
        "IHostEnvironment",
        "IHostApplicationLifetime",
        "IConfiguration",
        "IServiceProvider",
        "IHttpClientFactory",
        "IStringLocalizer",
        "IOptions",
        "IOptionsMonitor",
        "IOptionsSnapshot",
        "IHost",
        "CancellationToken"
    ];

    private static readonly HashSet<string> PrimitiveTypeNames = new(StringComparer.Ordinal)
    {
        "String", "Int32", "Int64", "Int16", "UInt32", "UInt64", "UInt16",
        "Boolean", "Single", "Double", "Decimal", "Byte", "SByte", "Char",
        "Object", "Void", "HttpClient", "TimeSpan", "DateTime", "Guid",
        "IServiceScopeFactory", "IServiceScope", "CancellationToken"
    };

    public static bool IsFrameworkProvided(string syntacticName, string? fullyQualifiedName = null)
    {
        var check = syntacticName;
        if (string.IsNullOrEmpty(check) && !string.IsNullOrEmpty(fullyQualifiedName))
        {
            var lastDot = fullyQualifiedName.LastIndexOf('.');
            check = lastDot >= 0 ? fullyQualifiedName[(lastDot + 1)..] : fullyQualifiedName;
        }

        if (string.IsNullOrEmpty(check))
            return false;

        if (PrimitiveTypeNames.Contains(check))
            return true;

        if (check.EndsWith(">", StringComparison.Ordinal))
            return true;

        foreach (var prefix in SyntacticPrefixes)
        {
            if (check.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
