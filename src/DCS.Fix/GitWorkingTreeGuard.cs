namespace DCS.Fix;

public static class GitWorkingTreeGuard
{
    public static bool IsClean(string repoPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "-C \"" + repoPath + "\" status --porcelain",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"git status failed: {proc.StandardError.ReadToEnd()}");

        return string.IsNullOrWhiteSpace(output);
    }
}
