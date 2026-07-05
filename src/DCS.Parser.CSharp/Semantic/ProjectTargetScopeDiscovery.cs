namespace DCS.Parser.CSharp.Semantic;

public static class ProjectTargetScopeDiscovery
{
    public static IReadOnlyList<ProjectTargetScope> DiscoverScopes(
        IReadOnlyList<(string path, string content)> sourceFiles,
        string repoRoot,
        SemanticExtractionOptions options,
        IReadOnlyDictionary<string, string>? commitCsprojContents = null)
    {
        var csprojFiles = FindCsprojFiles(repoRoot, sourceFiles, options, commitCsprojContents);
        if (csprojFiles.Count == 0)
            return [CreateOrphanScope(sourceFiles, repoRoot, options)];

        var scopes = new List<ProjectTargetScope>();
        foreach (var csproj in csprojFiles)
        {
            CsprojMetadata meta;
            try
            {
                var relativeCsproj = NormalizePath(Path.GetRelativePath(repoRoot, csproj));
                if (commitCsprojContents != null &&
                    commitCsprojContents.TryGetValue(relativeCsproj, out var content))
                    meta = CsprojMetadataReader.ReadFromContent(csproj, content, options.BuildConfiguration);
                else if (File.Exists(csproj))
                    meta = CsprojMetadataReader.Read(csproj, options.BuildConfiguration);
                else
                    continue;
            }
            catch
            {
                continue;
            }

            var projectDir = Path.GetDirectoryName(csproj)!;
            var tfms = SelectTargetFrameworks(meta, options);
            if (tfms.Count == 0)
                tfms = ["net8.0"];

            var projectRelativeDir = Path.GetRelativePath(repoRoot, projectDir);
            var memberFiles = AssignFilesToProject(sourceFiles, projectRelativeDir, meta);

            foreach (var tfm in tfms)
            {
                var scopeId = $"{NormalizePath(meta.CsprojPath)}|{tfm}|{options.BuildConfiguration}";
                var membershipHash = CsprojMetadataReader.ComputeSourceMembershipHash(
                    memberFiles.Select(f => f.path).ToList());

                scopes.Add(new ProjectTargetScope
                {
                    ScopeId = scopeId,
                    CsprojPath = meta.CsprojPath,
                    TargetFramework = tfm,
                    BuildConfiguration = options.BuildConfiguration,
                    AssemblyName = meta.AssemblyName ?? Path.GetFileNameWithoutExtension(meta.CsprojPath),
                    SourceMembershipProfileHash = membershipHash,
                    SourceFiles = memberFiles.Select(f => NormalizePath(f.path)).ToList(),
                    ProjectReferences = meta.ProjectReferences,
                    PackageReferences = meta.PackageReferences,
                    DefineConstants = meta.DefineConstants,
                    LangVersion = meta.LangVersion,
                    NullableEnabled = meta.Nullable ?? true,
                    ImplicitUsingsEnabled = meta.ImplicitUsings ?? true,
                    AllowUnsafeBlocks = meta.AllowUnsafeBlocks ?? false,
                    ProjectEvaluationIncomplete = meta.HasConditionalItems,
                    ImplicitUsingsUnmodeled = false,
                    IsTestProject = meta.IsTestProject
                });
            }
        }

        var assigned = scopes.SelectMany(s => s.SourceFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = sourceFiles.Where(f => !assigned.Contains(NormalizePath(f.path))).ToList();
        if (orphans.Count > 0)
            scopes.Add(CreateOrphanScope(orphans, repoRoot, options));

        return scopes;
    }

    private static List<string> SelectTargetFrameworks(CsprojMetadata meta, SemanticExtractionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TargetFramework))
            return [options.TargetFramework];

        if (options.AllTargetFrameworks)
            return meta.TargetFrameworks.ToList();

        return meta.TargetFrameworks.Count == 1
            ? meta.TargetFrameworks.ToList()
            : meta.TargetFrameworks.ToList();
    }

    private static List<(string path, string content)> AssignFilesToProject(
        IReadOnlyList<(string path, string content)> sourceFiles,
        string projectRelativeDir,
        CsprojMetadata meta)
    {
        var prefix = projectRelativeDir.Replace('\\', '/');
        if (prefix == ".") prefix = string.Empty;

        var files = sourceFiles
            .Where(f =>
            {
                var norm = f.path.Replace('\\', '/');
                if (string.IsNullOrEmpty(prefix))
                    return true;
                return norm.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
                       norm.Equals(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .Where(f => !meta.CompileRemoves.Any(r =>
                f.path.EndsWith(r.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (meta.CompileIncludes.Count > 0)
        {
            var includeSet = meta.CompileIncludes
                .Select(i => i.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            files = files.Where(f => includeSet.Any(i =>
                f.path.EndsWith(i, StringComparison.OrdinalIgnoreCase) ||
                f.path.Replace('\\', '/').EndsWith(i, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        return files;
    }

    private static ProjectTargetScope CreateOrphanScope(
        IReadOnlyList<(string path, string content)> files,
        string repoRoot,
        SemanticExtractionOptions options)
    {
        var bucketId = $"orphan:{NormalizePath(repoRoot)}";
        return new ProjectTargetScope
        {
            ScopeId = bucketId,
            CsprojPath = string.Empty,
            TargetFramework = options.TargetFramework ?? "net8.0",
            BuildConfiguration = options.BuildConfiguration,
            AssemblyName = "OrphanAssembly",
            SourceMembershipProfileHash = CsprojMetadataReader.ComputeSourceMembershipHash(
                files.Select(f => f.path).ToList()),
            SourceFiles = files.Select(f => NormalizePath(f.path)).ToList(),
            ProjectEvaluationIncomplete = true,
            ImplicitUsingsUnmodeled = true
        };
    }

    private static List<string> FindCsprojFiles(
        string repoRoot,
        IReadOnlyList<(string path, string content)> sourceFiles,
        SemanticExtractionOptions options,
        IReadOnlyDictionary<string, string>? commitCsprojContents = null)
    {
        List<string> found;
        if (commitCsprojContents != null)
        {
            found = commitCsprojContents.Keys
                .Select(relativePath => Path.GetFullPath(
                    Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else if (Directory.Exists(repoRoot))
        {
            found = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Select(Path.GetFullPath)
                .ToList();
        }
        else
        {
            found = [];
        }

        if (found.Count == 0 && commitCsprojContents == null)
        {
            var dirs = sourceFiles
                .Select(f => Path.GetDirectoryName(f.path.Replace('/', Path.DirectorySeparatorChar)))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToList();

            foreach (var dir in dirs)
            {
                var parts = dir!.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                for (var i = parts.Length; i >= 1; i--)
                {
                    var candidate = string.Join(Path.DirectorySeparatorChar, parts.Take(i));
                    var csproj = Directory.EnumerateFiles(candidate, "*.csproj").FirstOrDefault();
                    if (csproj != null)
                    {
                        found.Add(Path.GetFullPath(csproj));
                        break;
                    }
                }
            }

            found = found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (options.IncludeTests)
            return found;

        return found
            .Where(p => !ShellCompositionScope.IsTestCsprojPath(p))
            .ToList();
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').ToLowerInvariant();
}

public sealed record SemanticExtractionOptions
{
    public string BuildConfiguration { get; init; } = "Debug";
    public string? TargetFramework { get; init; }
    public bool AllTargetFrameworks { get; init; } = true;
    public bool IncludeTests { get; init; }
}
