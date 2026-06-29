namespace DCS.Cli;

internal sealed record CliOptions
{
    public string? RepoPath { get; init; }
    public string? Commit { get; init; }
    public string? FromSha { get; init; }
    public string? ToSha { get; init; }
    public string? FrameworksPath { get; init; }
    public string? CacheDir { get; init; }
    public bool NoCache { get; init; }
    public string? IrOut { get; init; }
    public string? RootClass { get; init; }
    public string? OutPath { get; init; }
    public bool FromIr { get; init; }
    public RepoLanguage Language { get; init; } = RepoLanguage.Auto;
    public string? ContextId { get; init; }
    public IReadOnlyList<string>? ContextRoot { get; init; }
    public bool ApplyFix { get; init; }
    public bool ForceFix { get; init; }
    public string? FixToken { get; init; }
    public bool FixAllDuplicates { get; init; }
    public string? TargetFramework { get; init; }
    public bool AllTargetFrameworks { get; init; } = true;
}

internal static class CliArgParser
{
    public static CliOptions ParseRepoCommand(string[] args, bool allowCommit = true, bool allowRoot = true)
    {
        string? repoPath = null, commit = null, frameworksPath = null, cacheDir = null;
        string? irOut = null, rootClass = null, contextId = null, contextRoot = null;
        var noCache = false;
        var language = RepoLanguage.Auto;
        string? targetFramework = null;
        var allTargetFrameworks = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--commit" or "-c" when allowCommit && i + 1 < args.Length:
                    commit = args[++i];
                    break;
                case "--target-framework" when i + 1 < args.Length:
                    targetFramework = args[++i];
                    allTargetFrameworks = false;
                    break;
                case "--all-target-frameworks":
                    allTargetFrameworks = true;
                    targetFramework = null;
                    break;
                case "--frameworks" when i + 1 < args.Length:
                    frameworksPath = args[++i];
                    break;
                case "--cache-dir" when i + 1 < args.Length:
                    cacheDir = args[++i];
                    break;
                case "--no-cache":
                    noCache = true;
                    break;
                case "--ir-out" when i + 1 < args.Length:
                    irOut = args[++i];
                    break;
                case "--root" when allowRoot && i + 1 < args.Length:
                    rootClass = args[++i];
                    break;
                case "--language" when i + 1 < args.Length:
                    language = RepoLanguageDetector.ParseLanguageFlag(args[++i]);
                    break;
                case "--context" when i + 1 < args.Length:
                    contextId = args[++i];
                    break;
                case "--context-root" when i + 1 < args.Length:
                    contextRoot = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith('-') && repoPath == null)
                        repoPath = args[i];
                    break;
            }
        }

        return new CliOptions
        {
            RepoPath = repoPath,
            Commit = commit,
            FrameworksPath = frameworksPath,
            CacheDir = cacheDir,
            NoCache = noCache,
            IrOut = irOut,
            RootClass = rootClass,
            Language = language,
            ContextId = contextId,
            ContextRoot = contextRoot == null ? null : [contextRoot],
            TargetFramework = targetFramework,
            AllTargetFrameworks = allTargetFrameworks
        };
    }

    public static CliOptions ParseDiffCommand(string[] args)
    {
        string? repoPath = null, fromSha = null, toSha = null, frameworksPath = null, cacheDir = null;
        string? irOut = null, contextId = null, contextRoot = null;
        var noCache = false;
        var language = RepoLanguage.Auto;
        string? targetFramework = null;
        var allTargetFrameworks = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Length:
                    fromSha = args[++i];
                    break;
                case "--target-framework" when i + 1 < args.Length:
                    targetFramework = args[++i];
                    allTargetFrameworks = false;
                    break;
                case "--all-target-frameworks":
                    allTargetFrameworks = true;
                    targetFramework = null;
                    break;
                case "--to" when i + 1 < args.Length:
                    toSha = args[++i];
                    break;
                case "--frameworks" when i + 1 < args.Length:
                    frameworksPath = args[++i];
                    break;
                case "--cache-dir" when i + 1 < args.Length:
                    cacheDir = args[++i];
                    break;
                case "--no-cache":
                    noCache = true;
                    break;
                case "--ir-out" when i + 1 < args.Length:
                    irOut = args[++i];
                    break;
                case "--language" when i + 1 < args.Length:
                    language = RepoLanguageDetector.ParseLanguageFlag(args[++i]);
                    break;
                case "--context" when i + 1 < args.Length:
                    contextId = args[++i];
                    break;
                case "--context-root" when i + 1 < args.Length:
                    contextRoot = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith('-') && repoPath == null)
                        repoPath = args[i];
                    break;
            }
        }

        return new CliOptions
        {
            RepoPath = repoPath,
            FromSha = fromSha,
            ToSha = toSha,
            FrameworksPath = frameworksPath,
            CacheDir = cacheDir,
            NoCache = noCache,
            IrOut = irOut,
            Language = language,
            ContextId = contextId,
            ContextRoot = contextRoot == null ? null : [contextRoot],
            TargetFramework = targetFramework,
            AllTargetFrameworks = allTargetFrameworks
        };
    }

    public static CliOptions ParseFixCommand(string[] args)
    {
        var baseOptions = ParseRepoCommand(args, allowCommit: false, allowRoot: false);
        var apply = false;
        var force = false;
        var fixAll = false;
        string? token = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply":
                    apply = true;
                    break;
                case "--preview":
                    apply = false;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--all-duplicates":
                    fixAll = true;
                    break;
                case "--token" when i + 1 < args.Length:
                    token = args[++i];
                    break;
            }
        }

        return baseOptions with
        {
            ApplyFix = apply,
            ForceFix = force,
            FixToken = token,
            FixAllDuplicates = fixAll,
            Language = baseOptions.Language == RepoLanguage.Auto ? RepoLanguage.CSharp : baseOptions.Language
        };
    }

    public static CliOptions ParseVizCommand(string[] args)
    {
        string? source = null, outPath = null, commit = null, rootClass = null;
        string? frameworksPath = null, cacheDir = null, contextId = null, contextRoot = null;
        var fromIr = false;
        var noCache = false;
        var language = RepoLanguage.Auto;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--commit" when i + 1 < args.Length:
                    commit = args[++i];
                    break;
                case "--root" when i + 1 < args.Length:
                    rootClass = args[++i];
                    break;
                case "--frameworks" when i + 1 < args.Length:
                    frameworksPath = args[++i];
                    break;
                case "--cache-dir" when i + 1 < args.Length:
                    cacheDir = args[++i];
                    break;
                case "--no-cache":
                    noCache = true;
                    break;
                case "--language" when i + 1 < args.Length:
                    language = RepoLanguageDetector.ParseLanguageFlag(args[++i]);
                    break;
                case "--context" when i + 1 < args.Length:
                    contextId = args[++i];
                    break;
                case "--context-root" when i + 1 < args.Length:
                    contextRoot = args[++i];
                    break;
                case "--ir":
                    fromIr = true;
                    break;
                default:
                    if (!args[i].StartsWith('-') && source == null)
                        source = args[i];
                    break;
            }
        }

        return new CliOptions
        {
            RepoPath = source,
            Commit = commit,
            FrameworksPath = frameworksPath,
            CacheDir = cacheDir,
            NoCache = noCache,
            RootClass = rootClass,
            OutPath = outPath,
            FromIr = fromIr,
            Language = language,
            ContextId = contextId,
            ContextRoot = contextRoot == null ? null : [contextRoot]
        };
    }
}
