using System.Text.RegularExpressions;

namespace DCS.Fix;

internal static partial class ProjectDefineConstantsResolver
{
    private static readonly Regex DefineConstantsRegex =
        DefineConstantsPattern();

    internal static bool DefinesConstant(string repoRoot, string relativeFilePath, string constant)
    {
        var defines = ResolveForFile(repoRoot, relativeFilePath);
        return defines.Contains(constant);
    }

    internal static IReadOnlySet<string> ResolveForFile(string repoRoot, string relativeFilePath)
    {
        var absoluteDir = Path.IsPathRooted(relativeFilePath)
            ? Path.GetDirectoryName(relativeFilePath)
            : Path.Combine(repoRoot, Path.GetDirectoryName(relativeFilePath) ?? string.Empty);

        var repoFull = Path.GetFullPath(repoRoot);

        while (!string.IsNullOrEmpty(absoluteDir))
        {
            var fullDir = Path.GetFullPath(absoluteDir);
            if (!fullDir.StartsWith(repoFull, StringComparison.OrdinalIgnoreCase))
                break;

            var csprojs = Directory.GetFiles(absoluteDir, "*.csproj");
            if (csprojs.Length > 0)
                return ParseDefineConstants(File.ReadAllText(csprojs[0]));

            absoluteDir = Directory.GetParent(absoluteDir)?.FullName;
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ParseDefineConstants(string csprojContent)
    {
        var match = DefineConstantsRegex.Match(csprojContent);
        if (!match.Success)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return match.Groups[1].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<DefineConstants>([^<]*)</DefineConstants>", RegexOptions.IgnoreCase)]
    private static partial Regex DefineConstantsPattern();
}
