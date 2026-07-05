namespace DCS.Verification;

/// <summary>
/// xUnit collections that serialize corpus gate tests hitting the same commit/cache fingerprint.
/// </summary>
public static class CorpusGateCollection
{
    public const string CsharpMigration = "corpus-gate/csharp-migration";
    public const string CsharpNegativeControl = "corpus-gate/csharp-negative-control";
    public const string JavaSpring = "corpus-gate/java-spring";
}

[CollectionDefinition(CorpusGateCollection.CsharpMigration, DisableParallelization = true)]
public sealed class CsharpMigrationCorpusGateCollection;

[CollectionDefinition(CorpusGateCollection.CsharpNegativeControl, DisableParallelization = true)]
public sealed class CsharpNegativeControlCorpusGateCollection;

[CollectionDefinition(CorpusGateCollection.JavaSpring, DisableParallelization = true)]
public sealed class JavaSpringCorpusGateCollection;
