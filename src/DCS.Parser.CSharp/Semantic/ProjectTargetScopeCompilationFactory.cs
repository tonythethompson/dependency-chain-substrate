using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynParseOptions = Microsoft.CodeAnalysis.CSharp.CSharpParseOptions;

namespace DCS.Parser.CSharp.Semantic;

public sealed class ScopeCompilationResult
{
    public required ProjectTargetScope Scope { get; init; }
    public required CSharpCompilation Compilation { get; init; }
    public required IReadOnlyDictionary<string, SemanticModel> ModelsByPath { get; init; }
    public required ReferenceProfile ReferenceProfile { get; init; }
}

public static class ProjectTargetScopeCompilationFactory
{
    public static ScopeCompilationResult Build(
        ProjectTargetScope scope,
        IReadOnlyDictionary<string, string> fileContents,
        IReadOnlyList<MetadataReference> additionalProjectRefs)
    {
        var profile = ReferenceProfileProvider.Get(scope);
        var parseOptions = CreateParseOptions(scope);
        var trees = new List<SyntaxTree>();

        var implicitTree = ReferenceProfileProvider.CreateImplicitUsingsTree(scope, parseOptions);
        if (implicitTree != null)
            trees.Add(implicitTree);

        foreach (var relativePath in scope.SourceFiles)
        {
            if (!fileContents.TryGetValue(relativePath, out var content))
                continue;
            trees.Add(CSharpSyntaxTree.ParseText(content, parseOptions, path: relativePath));
        }

        var refs = profile.References.Concat(additionalProjectRefs).ToList();
        var compilation = CSharpCompilation.Create(
            scope.AssemblyName,
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(scope.NullableEnabled
                    ? NullableContextOptions.Enable
                    : NullableContextOptions.Disable)
                .WithAllowUnsafe(scope.AllowUnsafeBlocks));

        var models = trees
            .Where(t => t.FilePath != "__implicit_usings__.cs")
            .ToDictionary(t => NormalizePath(t.FilePath!), t => compilation.GetSemanticModel(t), StringComparer.OrdinalIgnoreCase);

        return new ScopeCompilationResult
        {
            Scope = scope,
            Compilation = compilation,
            ModelsByPath = models,
            ReferenceProfile = profile
        };
    }

    private static RoslynParseOptions CreateParseOptions(ProjectTargetScope scope)
    {
        var langVersion = ParseLangVersion(scope.LangVersion);
        var preprocessorSymbols = scope.DefineConstants.ToList();
        return new RoslynParseOptions(langVersion)
            .WithPreprocessorSymbols(preprocessorSymbols);
    }

    private static LanguageVersion ParseLangVersion(string? version) => version switch
    {
        "12.0" or "12" => LanguageVersion.CSharp12,
        "11.0" or "11" => LanguageVersion.CSharp11,
        "10.0" or "10" => LanguageVersion.CSharp10,
        "9.0" or "9" => LanguageVersion.CSharp9,
        _ => LanguageVersion.CSharp12
    };

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');
}
