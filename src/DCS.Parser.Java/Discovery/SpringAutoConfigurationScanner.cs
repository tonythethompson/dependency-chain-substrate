using DCS.Core.IR;

namespace DCS.Parser.Java.Discovery;

internal static class SpringAutoConfigurationScanner
{
    public static List<BlindSpotReport> Scan(string rootPath, string contextId)
    {
        var reports = new List<BlindSpotReport>();
        if (!Directory.Exists(rootPath))
            return reports;

        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (IsUnderBuildOutput(path))
                continue;

            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("/META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("/META-INF/spring.factories", StringComparison.OrdinalIgnoreCase))
            {
                reports.Add(new BlindSpotReport
                {
                    Pattern = "auto_configuration",
                    Description = $"Boot auto-configuration resource not statically expanded (context {contextId}).",
                    Location = new SourceRef { FilePath = Path.GetRelativePath(rootPath, path) }
                });
            }
        }

        return reports;
    }

    private static bool IsUnderBuildOutput(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment is "target" or "build" or ".gradle")
                return true;
        }

        return false;
    }
}
