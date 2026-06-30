using DCS.Core.IR;

namespace DCS.Analysis;

public sealed class FindingPolicyOptions
{
    public bool Strict { get; init; }

    public static FindingPolicyOptions Default { get; } = new();

    public static FindingPolicyOptions StrictMode { get; } = new() { Strict = true };
}

/// <summary>
/// Classifies findings and suppresses expected DI patterns from actionable output.
/// </summary>
public static class FindingPolicy
{
    private static readonly HashSet<string> InformationalBlindSpotPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "factory_lambda",
        "factory_lambda_shallow",
        "extension_method",
    };

    private static readonly HashSet<string> NonActionableUnresolvedTypes = new(StringComparer.Ordinal)
    {
        "String", "Int32", "Int64", "Int16", "UInt32", "UInt64", "UInt16",
        "Boolean", "Single", "Double", "Decimal", "Byte", "SByte", "Char",
        "Object", "Void", "HttpClient", "TimeSpan", "DateTime", "Guid",
        "IServiceScopeFactory", "IServiceScope", "CancellationToken",
        "ILogger", "IConfiguration", "IServiceProvider", "IHostEnvironment",
        "IHttpClientFactory", "IStringLocalizer", "IOptions", "IOptionsMonitor",
        "IOptionsSnapshot", "IHost", "IHostApplicationLifetime",
    };

    private static readonly HashSet<string> SecondaryRootFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program.cs", "Startup.cs", "AppHost.cs", "ServiceRegistration.cs",
        "DependencyInjection.cs",
    };

    public static bool IsInformationalBlindSpot(string pattern, FindingPolicyOptions? options = null) =>
        !IsStrict(options) && InformationalBlindSpotPatterns.Contains(pattern);

    public static bool IsSecondaryReachabilityRootFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var fileName = Path.GetFileName(filePath);
        if (SecondaryRootFileNames.Contains(fileName))
            return true;

        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("Composition", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("ServiceRegistration", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIntentionalTryAddOverride(
        IReadOnlyList<RegistrationNode> nodes,
        FindingPolicyOptions? options = null)
    {
        if (IsStrict(options) || nodes.Count < 2)
            return false;

        var tryAdd = nodes.Count(n => n.Annotations.GetValueOrDefault("conditional") == "try_add");
        var explicitCount = nodes.Count - tryAdd;
        return tryAdd >= 1 && explicitCount == 1;
    }

    public static IEnumerable<BlindSpotReport> ActionableBlindSpots(
        IEnumerable<BlindSpotReport> blindSpots,
        FindingPolicyOptions? options = null) =>
        blindSpots.Where(b => !IsInformationalBlindSpot(b.Pattern, options));

    public static bool IsActionableUnresolved(
        string shortName,
        string? fullyQualifiedName = null,
        FindingPolicyOptions? options = null)
    {
        if (IsStrict(options))
            return !string.IsNullOrEmpty(shortName);

        if (string.IsNullOrEmpty(shortName))
            return false;

        if (NonActionableUnresolvedTypes.Contains(shortName))
            return false;

        var genericBase = TryGetGenericDefinitionName(shortName);
        if (genericBase != null)
        {
            if (NonActionableUnresolvedTypes.Contains(genericBase))
                return false;

            if (genericBase.StartsWith("ILogger", StringComparison.Ordinal) ||
                genericBase.StartsWith("IOptions", StringComparison.Ordinal) ||
                genericBase.StartsWith("IStringLocalizer", StringComparison.Ordinal))
                return false;
        }

        return !shortName.StartsWith("ILogger", StringComparison.Ordinal) &&
               !shortName.StartsWith("IOptions", StringComparison.Ordinal) &&
               !shortName.StartsWith("IStringLocalizer", StringComparison.Ordinal);
    }

    private static string? TryGetGenericDefinitionName(string typeName)
    {
        var angle = typeName.IndexOf('<');
        return angle > 0 ? typeName[..angle] : null;
    }

    public static bool IsParserLimitUnresolved(
        RegistrationNode consumer,
        UnresolvedInjection unresolved,
        RegistrationGraph graph)
    {
        if (unresolved.Reason == "semantic_unresolved")
            return true;

        if (consumer.ParserConfidence == Confidence.BlindSpot)
            return true;

        if (consumer.Annotations.GetValueOrDefault("pattern") is { } pattern &&
            InformationalBlindSpotPatterns.Contains(pattern))
            return true;

        return graph.BlindSpots.Any(b =>
            b.Location?.FilePath != null &&
            consumer.SourceLocation?.FilePath != null &&
            string.Equals(b.Location.FilePath, consumer.SourceLocation.FilePath, StringComparison.OrdinalIgnoreCase) &&
            b.Location.Line == consumer.SourceLocation.Line);
    }

    private static bool IsStrict(FindingPolicyOptions? options) =>
        options?.Strict == true;
}
