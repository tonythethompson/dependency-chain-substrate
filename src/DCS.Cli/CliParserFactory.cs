using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Parser.CSharp;
using DCS.Parser.Java;

namespace DCS.Cli;

internal static class CliParserFactory
{
    internal static IStaticParser Create(CliOptions options)
    {
        var boundaries = ProgramCommands.LoadBoundaries(options.FrameworksPath);
        var language = RepoLanguageDetector.Resolve(options.RepoPath, options.Language);

        Action<string>? onCacheHit = sha =>
            Console.Error.WriteLine($"[DCS] Cache hit for {sha[..Math.Min(8, sha.Length)]}");

        return language switch
        {
            RepoLanguage.Java => new SpringStaticParser(new JavaParseOptions
            {
                Boundaries = boundaries,
                CacheDirectory = options.CacheDir,
                NoCache = options.NoCache,
                OnCacheHit = onCacheHit,
                ContextRoots = options.ContextRoot
            }),
            RepoLanguage.CSharp => new CSharpStaticParser(new CSharpParseOptions
            {
                Boundaries = boundaries,
                CacheDirectory = options.CacheDir,
                NoCache = options.NoCache,
                OnCacheHit = onCacheHit
            }),
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    internal static ParseResult ExtractParseResult(IStaticParser parser, CliOptions options)
    {
        if (options.RepoPath == null)
            throw new InvalidOperationException("Repository path is required.");

        if (options.Commit != null)
        {
            Console.Error.WriteLine($"[DCS] Extracting {options.Commit[..Math.Min(8, options.Commit.Length)]}...");
            return parser.ParseCommit(options.RepoPath, options.Commit);
        }

        Console.Error.WriteLine("[DCS] Parsing working directory...");
        return parser.ParseDirectory(options.RepoPath);
    }

    internal static RegistrationGraph SelectGraph(ParseResult result, CliOptions options)
    {
        if (options.ContextId != null)
        {
            var match = result.ContextGraphs.FirstOrDefault(c =>
                string.Equals(c.ContextId, options.ContextId, StringComparison.Ordinal));
            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Context \"{options.ContextId}\" not found. Available: " +
                    string.Join(", ", result.ContextGraphs.Select(c => c.ContextId)));
            }

            return match.Graph;
        }

        if (result.ContextGraphs.Count == 1)
            return result.ContextGraphs[0].Graph;

        throw new InvalidOperationException(
            "Multiple application contexts found; pass --context <id>. " +
            $"Contexts: {string.Join(", ", result.ContextGraphs.Select(c => c.ContextId))}");
    }
}
