using System.Diagnostics;

namespace DCS.Fix;

public static class FixBuildVerifier
{
    public static void VerifyOrRollback(
        string repoRoot,
        IReadOnlyList<FilePatch> patches,
        Func<string, BuildVerificationResult> buildRunner)
    {
        var result = buildRunner(repoRoot);
        if (result.Succeeded)
            return;

        FixSafetyGuard.RollbackPatches(repoRoot, patches);
        var detail = string.IsNullOrWhiteSpace(result.Output)
            ? $"exit code {result.ExitCode}"
            : result.Output.Trim();
        throw new InvalidOperationException($"Fix rolled back: build verification failed ({detail}).");
    }

    public static BuildVerificationResult RunDotnetBuild(string repoRoot)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new BuildVerificationResult(
            process.ExitCode == 0,
            process.ExitCode,
            string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))));
    }
}
