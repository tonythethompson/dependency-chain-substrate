using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Core.Serialization;
using System.Runtime.InteropServices;

namespace DCS.Core.Caching;

public static class ExtractionCache
{
    public static string GetDefaultCacheDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dependency-chain-substrate",
                "cache");
        }

        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var baseDir = string.IsNullOrWhiteSpace(xdgCache)
            ? Path.Combine(home, ".cache")
            : xdgCache;

        return Path.Combine(baseDir, "dependency-chain-substrate");
    }

    public static string ResolveCacheDirectory(string? overridePath) =>
        string.IsNullOrWhiteSpace(overridePath)
            ? GetDefaultCacheDirectory()
            : overridePath;

    public static string GetCacheFilePath(string cacheDirectory, string commitSha, string parserVersion, string? fingerprint = null)
    {
        var suffix = string.IsNullOrEmpty(fingerprint) ? parserVersion : $"{parserVersion}_{fingerprint[..Math.Min(8, fingerprint.Length)]}";
        return Path.Combine(cacheDirectory, $"{commitSha}_{suffix}.json");
    }

    public static ParseResult? TryReadResult(string commitSha, string parserVersion, string cacheDirectory, string? fingerprint = null)
    {
        var path = GetCacheFilePath(cacheDirectory, commitSha, parserVersion, fingerprint);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var result = ParseResultSerializer.Deserialize(json);
            if (result == null)
                return null;

            var versionOk = result.ContextGraphs.Count == 0 ||
                            string.Equals(result.ContextGraphs[0].Graph.ParserVersion, parserVersion, StringComparison.Ordinal);
            if (!versionOk)
                return null;

            return result with
            {
                ContextGraphs = result.ContextGraphs
                    .Select(cg => cg with
                    {
                        Graph = cg.Graph with { CommitSha = commitSha }
                    })
                    .ToList()
            };
        }
        catch
        {
            return null;
        }
    }

    public static RegistrationGraph? TryRead(string commitSha, string parserVersion, string cacheDirectory, string? fingerprint = null) =>
        TryReadResult(commitSha, parserVersion, cacheDirectory, fingerprint)?.SingleGraphOrDefault();

    public static void Write(ParseResult result, string cacheDirectory, string? fingerprint = null)
    {
        var commitSha = result.ContextGraphs.FirstOrDefault()?.Graph.CommitSha;
        if (string.IsNullOrEmpty(commitSha))
            return;

        var parserVersion = result.ContextGraphs[0].Graph.ParserVersion;
        Directory.CreateDirectory(cacheDirectory);
        var path = GetCacheFilePath(cacheDirectory, commitSha, parserVersion, fingerprint);
        File.WriteAllText(path, ParseResultSerializer.Serialize(result));
    }

    public static void Write(RegistrationGraph graph, string cacheDirectory, string? fingerprint = null) =>
        Write(CSharpParseResultFactory.Wrap(graph), cacheDirectory, fingerprint);
}
