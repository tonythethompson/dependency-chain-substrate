using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynParseOptions = Microsoft.CodeAnalysis.CSharp.CSharpParseOptions;

namespace DCS.Parser.CSharp.Semantic;

public sealed record ReferenceProfile
{
    public required string ProfileId { get; init; }
    public required string TargetFramework { get; init; }
    public required IReadOnlyList<MetadataReference> References { get; init; }
    public required string Fingerprint { get; init; }
    public bool ImplicitUsingsInjected { get; init; }
}

public static class ReferenceProfileProvider
{
    private static readonly string[] ImplicitUsingsForNet8 =
    [
        "global using System;",
        "global using System.Collections.Generic;",
        "global using System.IO;",
        "global using System.Linq;",
        "global using System.Net.Http;",
        "global using System.Threading;",
        "global using System.Threading.Tasks;"
    ];

    public static ReferenceProfile Get(ProjectTargetScope scope)
    {
        var refs = ResolveFrameworkReferences(scope.TargetFramework);
        var fingerprintParts = new List<string>
        {
            scope.TargetFramework,
            scope.LangVersion ?? "default",
            scope.NullableEnabled ? "nullable" : "nonnullable",
            string.Join(",", scope.DefineConstants.OrderBy(c => c, StringComparer.Ordinal)),
            scope.ImplicitUsingsEnabled ? "implicit" : "no-implicit"
        };

        foreach (var r in refs)
        {
            if (r.Display?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true &&
                File.Exists(r.Display))
            {
                fingerprintParts.Add(Path.GetFileName(r.Display) + ":" +
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(r.Display)))[..8]);
            }
        }

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", fingerprintParts))))[..16];

        return new ReferenceProfile
        {
            ProfileId = $"tfm-{scope.TargetFramework}",
            TargetFramework = scope.TargetFramework,
            References = refs,
            Fingerprint = fingerprint,
            ImplicitUsingsInjected = scope.ImplicitUsingsEnabled && !scope.ImplicitUsingsUnmodeled
        };
    }

    public static SyntaxTree? CreateImplicitUsingsTree(ProjectTargetScope scope, RoslynParseOptions? parseOptions = null)
    {
        if (!scope.ImplicitUsingsEnabled || scope.ImplicitUsingsUnmodeled)
            return null;

        var content = string.Join('\n', ImplicitUsingsForNet8) + '\n';
        return CSharpSyntaxTree.ParseText(content, parseOptions ?? new RoslynParseOptions(), path: "__implicit_usings__.cs");
    }

    private static List<MetadataReference> ResolveFrameworkReferences(string targetFramework)
    {
        var refs = new List<MetadataReference>();
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        AddRefPackAssemblies(refs, dotnetRoot, "Microsoft.NETCore.App.Ref", targetFramework);
        AddRefPackAssemblies(refs, dotnetRoot, "Microsoft.AspNetCore.App.Ref", targetFramework);

        if (targetFramework.Contains('-', StringComparison.Ordinal))
        {
            var portableTfm = CrossTfmProjectReferenceResolver.GetPortableTargetFrameworkMoniker(targetFramework);
            AddRefPackAssemblies(refs, dotnetRoot, "Microsoft.WindowsDesktop.App.Ref", portableTfm);
            AddRefPackAssemblies(refs, dotnetRoot, "Microsoft.WindowsDesktop.App.Ref", targetFramework);
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        refs.Add(MetadataReference.CreateFromFile(
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly.Location));

        AddAssemblyIfPresent(refs, typeof(Microsoft.Extensions.Logging.ILogger).Assembly);
        AddAssemblyIfPresent(refs, typeof(Microsoft.Extensions.Hosting.IHostEnvironment).Assembly);
        AddAssemblyIfPresent(refs, typeof(Microsoft.Extensions.Options.IOptions<>).Assembly);
        AddAssemblyIfPresent(refs, typeof(Microsoft.Extensions.Localization.IStringLocalizer).Assembly);
        AddAssemblyIfPresent(refs, typeof(Microsoft.Extensions.Configuration.IConfiguration).Assembly);

        return refs;
    }

    private static void AddRefPackAssemblies(
        List<MetadataReference> refs,
        string dotnetRoot,
        string packName,
        string targetFramework)
    {
        var packRoot = Path.Combine(dotnetRoot, "packs", packName);
        if (!Directory.Exists(packRoot))
            return;

        foreach (var versionDir in Directory.GetDirectories(packRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            var refDir = Path.Combine(versionDir, "ref", targetFramework);
            if (!Directory.Exists(refDir))
                continue;

            foreach (var dll in Directory.EnumerateFiles(refDir, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    if (refs.Any(r => string.Equals(r.Display, dll, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    refs.Add(MetadataReference.CreateFromFile(dll));
                }
                catch { /* skip invalid refs */ }
            }

            return;
        }
    }

    private static void AddAssemblyIfPresent(List<MetadataReference> refs, System.Reflection.Assembly assembly)
    {
        var location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return;

        if (refs.Any(r => string.Equals(r.Display, location, StringComparison.OrdinalIgnoreCase)))
            return;

        refs.Add(MetadataReference.CreateFromFile(location));
    }
}
