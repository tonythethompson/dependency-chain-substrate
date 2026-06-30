namespace DCS.Parser.CSharp.Semantic;

/// <summary>
/// Maps projects and source files to runtime shells for duplicate/leakage grouping.
/// </summary>
public static class ShellCompositionScope
{
    /// <summary>
    /// Scope for duplicate/leak grouping. Production code in the same TFM shares one bucket;
    /// framework tags on nodes distinguish WinUI vs Avalonia for LEAKED detection.
    /// </summary>
    public static string RuntimeScopeForDuplicate(ProjectTargetScope scope, string sourceFilePath)
    {
        if (scope.IsTestProject)
            return scope.ScopeId;

        _ = sourceFilePath;
        return $"runtime|{scope.TargetFramework.ToLowerInvariant()}";
    }

    public static bool IsTestCsprojPath(string csprojPath)
    {
        var name = Path.GetFileNameWithoutExtension(csprojPath);
        if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Benchmark", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.StartsWith("tmp_", StringComparison.OrdinalIgnoreCase))
            return true;

        var dir = Path.GetDirectoryName(csprojPath)?.Replace('\\', '/').ToLowerInvariant() ?? string.Empty;
        return dir.Contains("/tests/", StringComparison.Ordinal) ||
               dir.EndsWith("/tests", StringComparison.Ordinal);
    }
}
