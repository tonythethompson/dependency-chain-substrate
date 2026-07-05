namespace DCS.Fix;

public sealed class FixFileContext
{
    private readonly string _repoRoot;
    private readonly Func<string, string> _read;

    public FixFileContext(string repoRoot, Func<string, string>? read = null)
    {
        _repoRoot = repoRoot;
        _read = read ?? (relativePath => File.ReadAllText(ResolveAbsolute(relativePath)));
    }

    public string Read(string relativePath) => _read(relativePath);

    public static string ResolveAbsolute(string repoRoot, string relativePath) =>
        Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(repoRoot, relativePath);

    internal string ResolveAbsolute(string relativePath) => ResolveAbsolute(_repoRoot, relativePath);
}
