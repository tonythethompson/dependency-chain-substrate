namespace DCS.Cli;

using DCS.Analysis;

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
    public bool AllTargetFrameworks { get; init; }
    /// <summary>When true, excludes test/benchmark projects (default for analyze).</summary>
    public bool ProductionOnly { get; init; } = true;
    public bool IncludeTests { get; init; }
    public ReportVerbosity Verbosity { get; init; } = ReportVerbosity.Actionable;
    public bool Strict { get; init; }
    public bool VerboseBlindSpots { get; init; }
    public bool Metrics { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Text;
    public string? ReportOut { get; init; }
    public string? TextOut { get; init; }
    public bool ContextAll { get; init; }
    public string? PathFrom { get; init; }
    public string? PathTo { get; init; }
}

internal enum OutputFormat
{
    Text,
    Json
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
        var allTargetFrameworks = false;
        var productionOnly = true;
        var includeTests = false;
        var verbosity = ReportVerbosity.Actionable;
        var strict = false;
        var verboseBlindSpots = false;
        var metrics = false;
        var format = OutputFormat.Text;
        string? reportOut = null;
        string? textOut = null;
        var contextAll = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--commit" or "-c" when allowCommit && i + 1 < args.Length:
                    commit = args[++i];
                    break;
                case "--production-only":
                    productionOnly = true;
                    includeTests = false;
                    allTargetFrameworks = false;
                    break;
                case "--include-tests":
                    includeTests = true;
                    productionOnly = false;
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
                    var ctxVal = args[++i];
                    if (string.Equals(ctxVal, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        contextAll = true;
                        contextId = null;
                    }
                    else
                    {
                        contextId = ctxVal;
                    }
                    break;
                case "--context-root" when i + 1 < args.Length:
                    contextRoot = args[++i];
                    break;
                case "--verbosity" when i + 1 < args.Length:
                    verbosity = ParseVerbosity(args[++i]);
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--verbose-blind-spots":
                    verboseBlindSpots = true;
                    break;
                case "--metrics":
                    metrics = true;
                    break;
                case "--format" when i + 1 < args.Length:
                    format = ParseFormat(args[++i]);
                    break;
                case "--report-out" when i + 1 < args.Length:
                    reportOut = args[++i];
                    break;
                case "--text-out" when i + 1 < args.Length:
                    textOut = args[++i];
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
            AllTargetFrameworks = allTargetFrameworks,
            ProductionOnly = productionOnly,
            IncludeTests = includeTests || !productionOnly,
            Verbosity = verbosity,
            Strict = strict,
            VerboseBlindSpots = verboseBlindSpots,
            Metrics = metrics,
            Format = format,
            ReportOut = reportOut,
            TextOut = textOut,
            ContextAll = contextAll
        };
    }

    private static ReportVerbosity ParseVerbosity(string value) => value.ToLowerInvariant() switch
    {
        "summary" => ReportVerbosity.Summary,
        "full" => ReportVerbosity.Full,
        "actionable" => ReportVerbosity.Actionable,
        _ => throw new ArgumentException($"Unknown verbosity: {value}. Use summary, actionable, or full.")
    };

    private static OutputFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "json" => OutputFormat.Json,
        "text" => OutputFormat.Text,
        _ => throw new ArgumentException($"Unknown format: {value}. Use text or json.")
    };

    public static CliOptions ParseDiffCommand(string[] args)
    {
        string? repoPath = null, fromSha = null, toSha = null, frameworksPath = null, cacheDir = null;
        string? irOut = null, contextId = null, contextRoot = null;
        var noCache = false;
        var language = RepoLanguage.Auto;
        string? targetFramework = null;
        var allTargetFrameworks = false;
        var productionOnly = true;
        var includeTests = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Length:
                    fromSha = args[++i];
                    break;
                case "--production-only":
                    productionOnly = true;
                    includeTests = false;
                    allTargetFrameworks = false;
                    break;
                case "--include-tests":
                    includeTests = true;
                    productionOnly = false;
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
            AllTargetFrameworks = allTargetFrameworks,
            ProductionOnly = productionOnly,
            IncludeTests = includeTests || !productionOnly
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

    public static CliOptions ParsePathCommand(string[] args)
    {
        string? repoPath = null, commit = null, frameworksPath = null, cacheDir = null;
        string? irOut = null, rootClass = null, contextId = null, contextRoot = null;
        string? pathFrom = null, pathTo = null;
        var noCache = false;
        var language = RepoLanguage.Auto;
        string? targetFramework = null;
        var allTargetFrameworks = false;
        var productionOnly = true;
        var includeTests = false;
        var format = OutputFormat.Text;
        string? reportOut = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--commit" or "-c" when i + 1 < args.Length:
                    commit = args[++i];
                    break;
                case "--production-only":
                    productionOnly = true;
                    includeTests = false;
                    allTargetFrameworks = false;
                    break;
                case "--include-tests":
                    includeTests = true;
                    productionOnly = false;
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
                case "--root" when i + 1 < args.Length:
                    rootClass = args[++i];
                    break;
                case "--context" when i + 1 < args.Length:
                    contextId = args[++i];
                    break;
                case "--context-root" when i + 1 < args.Length:
                    contextRoot = args[++i];
                    break;
                case "--format" when i + 1 < args.Length:
                    format = args[++i].Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? OutputFormat.Json
                        : OutputFormat.Text;
                    break;
                case "--report-out" when i + 1 < args.Length:
                    reportOut = args[++i];
                    break;
                case "--from" when i + 1 < args.Length:
                    pathFrom = args[++i];
                    break;
                case "--to" when i + 1 < args.Length:
                    pathTo = args[++i];
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
            AllTargetFrameworks = allTargetFrameworks,
            ProductionOnly = productionOnly,
            IncludeTests = includeTests,
            Format = format,
            ReportOut = reportOut,
            PathFrom = pathFrom,
            PathTo = pathTo
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
