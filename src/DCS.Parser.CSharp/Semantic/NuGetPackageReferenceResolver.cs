using Microsoft.CodeAnalysis;

namespace DCS.Parser.CSharp.Semantic;

/// <summary>
/// Resolves NuGet package assemblies from the local package cache for semantic compilation.
/// </summary>
internal static class NuGetPackageReferenceResolver
{
    private static readonly string[] AwsPackagePrefixes = ["AWSSDK.", "Amazon.Lambda."];

    public static void AddPackageReferences(
        List<MetadataReference> refs,
        IReadOnlyList<string> packageIds,
        string targetFramework)
    {
        if (packageIds.Count == 0)
            return;

        var nugetRoot = ResolveNuGetPackagesRoot();
        if (nugetRoot == null)
            return;

        foreach (var packageId in packageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsAwsOrLambdaPackage(packageId))
                continue;

            TryAddPackageAssemblies(refs, nugetRoot, packageId, targetFramework);
        }
    }

    private static bool IsAwsOrLambdaPackage(string packageId) =>
        AwsPackagePrefixes.Any(p => packageId.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string? ResolveNuGetPackagesRoot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, ".nuget", "packages");
        return Directory.Exists(path) ? path : null;
    }

    private static void TryAddPackageAssemblies(
        List<MetadataReference> refs,
        string nugetRoot,
        string packageId,
        string targetFramework)
    {
        var packageDir = Path.Combine(nugetRoot, packageId.ToLowerInvariant());
        if (!Directory.Exists(packageDir))
            return;

        var versionDir = Directory.GetDirectories(packageDir)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (versionDir == null)
            return;

        var libDir = Path.Combine(versionDir, "lib");
        if (!Directory.Exists(libDir))
            return;

        var tfmDir = FindBestTfmLibDir(libDir, targetFramework);
        if (tfmDir == null)
            return;

        foreach (var dll in Directory.EnumerateFiles(tfmDir, "*.dll"))
        {
            try
            {
                if (refs.Any(r => string.Equals(r.Display, dll, StringComparison.OrdinalIgnoreCase)))
                    continue;
                refs.Add(MetadataReference.CreateFromFile(dll));
            }
            catch { /* skip invalid refs */ }
        }
    }

    private static string? FindBestTfmLibDir(string libDir, string targetFramework)
    {
        var candidates = Directory.GetDirectories(libDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();

        if (candidates.Count == 0)
            return null;

        var portable = CrossTfmProjectReferenceResolver.GetPortableTargetFrameworkMoniker(targetFramework);
        var ordered = new[]
        {
            targetFramework,
            portable,
            "net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"
        };

        foreach (var tfm in ordered)
        {
            var match = candidates.FirstOrDefault(c =>
                string.Equals(c, tfm, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return Path.Combine(libDir, match);
        }

        return Path.Combine(libDir, candidates.OrderByDescending(c => c, StringComparer.Ordinal).First());
    }
}
