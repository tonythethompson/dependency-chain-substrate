namespace DCS.Verification;

/// <summary>
/// xUnit trait values for corpus gate tests. CI filters on these — not on class names.
/// Gate checkout pins live in <c>ci/corpus-gates.json</c>.
/// </summary>
public static class CorpusGateTraits
{
    public const string CategoryName = "Category";
    public const string CorpusIdName = "CorpusId";
    public const string CategoryValue = "CorpusGate";

    public const string CsharpMigration = "csharp-migration";
    /// <summary>Optional local gate — not in ci/corpus-gates.json. Aspirational semantic quality floor.</summary>
    public const string CsharpMigrationQuality = "csharp-migration-quality";
    public const string CsharpNegativeControl = "csharp-negative-control";
    public const string JavaSpring = "java-spring";
}
