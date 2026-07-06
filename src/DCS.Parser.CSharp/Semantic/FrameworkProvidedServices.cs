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
        "CancellationToken",
        "IMeterFactory"
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

        foreach (var prefix in SyntacticPrefixes)
        {
            if (!check.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            if (prefix is "IOptions" or "IOptionsMonitor" or "IOptionsSnapshot")
            {
                var genericArg = TryGetGenericArgumentName(check);
                if (genericArg != null && !IsFrameworkOptionsType(genericArg))
                    return false;
            }

            return true;
        }

        return false;
    }

    private static string? TryGetGenericArgumentName(string syntacticName)
    {
        var start = syntacticName.IndexOf('<');
        var end = syntacticName.LastIndexOf('>');
        if (start < 0 || end <= start)
            return null;
        return syntacticName[(start + 1)..end].TrimEnd('?');
    }

    private static bool IsFrameworkOptionsType(string typeName) =>
        typeName is "IConfiguration" or "IHostEnvironment" or "IWebHostEnvironment";
}
