using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DCS.Parser.CSharp.Semantic;

public sealed record CsprojMetadata
{
    public required string CsprojPath { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public string? AssemblyName { get; init; }
    public string? LangVersion { get; init; }
    public bool? Nullable { get; init; }
    public bool? ImplicitUsings { get; init; }
    public bool? AllowUnsafeBlocks { get; init; }
    public IReadOnlyList<string> DefineConstants { get; init; } = [];
    public IReadOnlyList<string> CompileIncludes { get; init; } = [];
    public IReadOnlyList<string> CompileRemoves { get; init; } = [];
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
    public IReadOnlyList<string> PackageReferences { get; init; } = [];
    public bool HasConditionalItems { get; init; }
    public bool HasUnresolvedImports { get; init; }
    public bool IsTestProject { get; init; }
}

public static class CsprojMetadataReader
{
    public static CsprojMetadata Read(string csprojPath, string buildConfiguration = "Debug") =>
        ReadFromContent(csprojPath, File.ReadAllText(csprojPath), buildConfiguration);

    public static CsprojMetadata ReadFromContent(
        string csprojPath,
        string csprojContent,
        string buildConfiguration = "Debug")
    {
        var doc = XDocument.Parse(csprojContent);
        var root = doc.Root ?? throw new InvalidOperationException($"Empty csproj: {csprojPath}");
        var ns = root.Name.Namespace;

        var propertyGroups = root.Elements(ns + "PropertyGroup").ToList();
        string? PickProperty(string name)
        {
            foreach (var pg in propertyGroups)
            {
                var condition = pg.Attribute("Condition")?.Value;
                if (condition != null &&
                    !condition.Contains(buildConfiguration, StringComparison.OrdinalIgnoreCase))
                    continue;
                var el = pg.Element(ns + name);
                if (el != null) return el.Value.Trim();
            }
            return propertyGroups
                .Where(pg => pg.Attribute("Condition") == null)
                .Select(pg => pg.Element(ns + name)?.Value.Trim())
                .FirstOrDefault(v => v != null);
        }

        var tfmPlural = PickProperty("TargetFrameworks");
        var tfmSingular = PickProperty("TargetFramework");
        var targetFrameworks = new List<string>();
        if (!string.IsNullOrWhiteSpace(tfmPlural))
            targetFrameworks.AddRange(tfmPlural.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        else if (!string.IsNullOrWhiteSpace(tfmSingular))
            targetFrameworks.Add(tfmSingular);

        var defineConstants = (PickProperty("DefineConstants") ?? "TRACE")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!defineConstants.Contains(buildConfiguration, StringComparer.OrdinalIgnoreCase))
            defineConstants.Add(buildConfiguration);

        var compileIncludes = root.Elements(ns + "ItemGroup")
            .SelectMany(g => g.Elements(ns + "Compile"))
            .Where(e => e.Attribute("Condition") == null)
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var compileRemoves = root.Elements(ns + "ItemGroup")
            .SelectMany(g => g.Elements(ns + "Compile"))
            .Where(e => e.Attribute("Remove") != null && e.Attribute("Condition") == null)
            .Select(e => e.Attribute("Remove")!.Value)
            .ToList();

        var projectRefs = root.Elements(ns + "ItemGroup")
            .SelectMany(g => g.Elements(ns + "ProjectReference"))
            .Where(e => e.Attribute("Condition") == null)
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csprojPath)!, v!)))
            .ToList();

        var packageRefs = root.Elements(ns + "ItemGroup")
            .SelectMany(g => g.Elements(ns + "PackageReference"))
            .Where(e => e.Attribute("Condition") == null)
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var hasConditional = root.Descendants()
            .Any(e => e.Attribute("Condition") != null &&
                      (e.Name.LocalName is "Compile" or "ProjectReference" or "None"));

        var imports = root.Elements(ns + "Import").Select(e => e.Attribute("Project")?.Value).ToList();
        var hasUnresolvedImports = imports.Any(i => i != null && i.Contains('$'));

        bool? ParseBool(string? v) => v switch
        {
            null => null,
            "enable" => true,
            "disable" => false,
            _ => bool.TryParse(v, out var b) ? b : null
        };

        return new CsprojMetadata
        {
            CsprojPath = csprojPath,
            TargetFrameworks = targetFrameworks,
            AssemblyName = PickProperty("AssemblyName"),
            LangVersion = PickProperty("LangVersion"),
            Nullable = ParseBool(PickProperty("Nullable")),
            ImplicitUsings = ParseBool(PickProperty("ImplicitUsings")),
            AllowUnsafeBlocks = ParseBool(PickProperty("AllowUnsafeBlocks")),
            DefineConstants = defineConstants,
            CompileIncludes = compileIncludes,
            CompileRemoves = compileRemoves,
            ProjectReferences = projectRefs,
            PackageReferences = packageRefs,
            HasConditionalItems = hasConditional,
            HasUnresolvedImports = hasUnresolvedImports,
            IsTestProject = ParseBool(PickProperty("IsTestProject")) == true ||
                            ShellCompositionScope.IsTestCsprojPath(csprojPath)
        };
    }

    public static string ComputeSourceMembershipHash(IReadOnlyList<string> relativePaths)
    {
        var sorted = relativePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted)));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
