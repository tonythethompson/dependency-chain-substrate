namespace DCS.Parser.CSharp.Semantic;

public static class TargetFrameworkSelector
{
    /// <summary>
    /// Parses <c>csharp|net10.0</c>, bare <c>net10.0</c>, or returns null.
    /// </summary>
    public static string? TryParseContextTargetFramework(string? contextId)
    {
        if (string.IsNullOrWhiteSpace(contextId))
            return null;

        if (contextId.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return contextId;

        var parts = contextId.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        if (!parts[0].Equals("csharp", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1];
    }

    public static string ToContextId(string targetFramework) =>
        $"csharp|{targetFramework}";

    /// <summary>
    /// When a single graph is requested, prefer portable TFMs (net10.0) over platform legs (net10.0-windows…).
    /// </summary>
    public static string SelectPrimaryTargetFramework(IEnumerable<string> targetFrameworks)
    {
        var tfms = targetFrameworks
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tfms.Count == 0)
            return "net8.0";

        var portable = tfms
            .Where(t => !t.Contains('-', StringComparison.Ordinal))
            .OrderByDescending(t => t, NetMonikerComparer.Instance)
            .FirstOrDefault();
        if (portable != null)
            return portable;

        return tfms.OrderByDescending(t => t, NetMonikerComparer.Instance).First();
    }

    private sealed class NetMonikerComparer : IComparer<string>
    {
        public static NetMonikerComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return CompareVersions(ParseNetVersion(x), ParseNetVersion(y));
        }

        private static (int Major, int Minor) ParseNetVersion(string tfm)
        {
            if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                return (0, 0);

            var rest = tfm[3..];
            var dot = rest.IndexOf('.');
            if (dot < 0)
                return int.TryParse(rest, out var majorOnly) ? (majorOnly, 0) : (0, 0);

            _ = int.TryParse(rest[..dot], out var major);
            _ = int.TryParse(rest[(dot + 1)..], out var minor);
            return (major, minor);
        }

        private static int CompareVersions((int Major, int Minor) a, (int Major, int Minor) b)
        {
            var major = a.Major.CompareTo(b.Major);
            return major != 0 ? major : a.Minor.CompareTo(b.Minor);
        }
    }
}
