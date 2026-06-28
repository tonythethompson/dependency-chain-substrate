using DCS.Analysis;
using DCS.Core.Serialization;
using DCS.Diff;
using DCS.Parser.CSharp;
using DCS.Viz;

var cliArgs = Environment.GetCommandLineArgs()[1..];

if (cliArgs.Length == 0 || cliArgs.Contains("--help") || cliArgs.Contains("-h"))
{
    PrintHelp();
    return 0;
}

var command = cliArgs[0];

return command switch
{
    "analyze" => await RunAnalyze(cliArgs[1..]),
    "dump-ir" => await RunDumpIr(cliArgs[1..]),
    "diff"    => await RunDiff(cliArgs[1..]),
    "viz"     => await RunViz(cliArgs[1..]),
    _         => ErrorExit($"Unknown command: {command}. Run 'dcs --help' for usage.")
};

// ── analyze ─────────────────────────────────────────────────────────────────

static async Task<int> RunAnalyze(string[] args)
{
    var (repoPath, commit, irOut, rootClass) = ParseAnalyzeArgs(args);
    if (repoPath == null) return ErrorExit("analyze requires <repo-path>");

    var parser = new CSharpStaticParser();
    Console.Error.WriteLine($"[DCS] Parsing {(commit != null ? $"commit {commit}" : "working directory")}...");

    var graph = commit != null
        ? parser.ParseCommit(repoPath, commit)
        : parser.ParseDirectory(repoPath);

    Console.Error.WriteLine($"[DCS] {graph.Nodes.Count} registrations, {graph.Edges.Count} edges, {graph.BlindSpots.Count} blind spots");

    var analyzer = new GraphAnalyzer(graph, rootClassOverride: rootClass);
    var result = analyzer.Analyze();

    PrintReport(graph, result);

    if (irOut != null)
        await WriteIr(graph, irOut);

    return result.HasErrors ? 1 : 0;
}

// ── dump-ir ──────────────────────────────────────────────────────────────────

static async Task<int> RunDumpIr(string[] args)
{
    var (repoPath, commit, irOut, _) = ParseAnalyzeArgs(args);
    if (repoPath == null) return ErrorExit("dump-ir requires <repo-path>");

    var parser = new CSharpStaticParser();
    var graph = commit != null
        ? parser.ParseCommit(repoPath, commit)
        : parser.ParseDirectory(repoPath);

    var json = IrSerializer.Serialize(graph);
    if (irOut != null)
    {
        await File.WriteAllTextAsync(irOut, json);
        Console.Error.WriteLine($"[DCS] IR written to {irOut}");
    }
    else
    {
        Console.WriteLine(json);
    }

    return 0;
}

// ── diff ──────────────────────────────────────────────────────────────────────

static async Task<int> RunDiff(string[] args)
{
    string? repoPath = null, fromSha = null, toSha = null, irOut = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--from" when i + 1 < args.Length: fromSha = args[++i]; break;
            case "--to"   when i + 1 < args.Length: toSha   = args[++i]; break;
            case "--ir-out" when i + 1 < args.Length: irOut = args[++i]; break;
            default:
                if (!args[i].StartsWith('-') && repoPath == null) repoPath = args[i];
                break;
        }
    }

    if (repoPath == null) return ErrorExit("diff requires <repo-path>");
    if (fromSha == null)  return ErrorExit("diff requires --from <sha>");
    if (toSha == null)    return ErrorExit("diff requires --to <sha>");

    var parser = new CSharpStaticParser();
    Console.Error.WriteLine($"[DCS] Extracting {fromSha[..Math.Min(8, fromSha.Length)]}...");
    var oldGraph = parser.ParseCommit(repoPath, fromSha);
    Console.Error.WriteLine($"[DCS] Extracting {toSha[..Math.Min(8, toSha.Length)]}...");
    var newGraph = parser.ParseCommit(repoPath, toSha);

    var diff = new GraphDiffer().Diff(oldGraph, newGraph);
    PrintDiff(diff);

    if (irOut != null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(diff, IrSerializer.Options);
        await File.WriteAllTextAsync(irOut, json);
        Console.Error.WriteLine($"[DCS] Diff written to {irOut}");
    }

    return diff.HasBreakingChanges ? 1 : 0;
}

// ── viz ───────────────────────────────────────────────────────────────────────

static async Task<int> RunViz(string[] args)
{
    string? source = null, outPath = null, commit = null, rootClass = null;
    bool fromIr = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--out"    when i + 1 < args.Length: outPath   = args[++i]; break;
            case "--commit" when i + 1 < args.Length: commit    = args[++i]; break;
            case "--root"   when i + 1 < args.Length: rootClass = args[++i]; break;
            case "--ir":                               fromIr    = true;      break;
            default:
                if (!args[i].StartsWith('-') && source == null) source = args[i];
                break;
        }
    }

    if (source == null) return ErrorExit("viz requires <repo-path> or <ir-file> --ir");

    DCS.Core.IR.RegistrationGraph graph;
    DCS.Analysis.AnalysisResult? analysis = null;

    if (fromIr)
    {
        var json = await File.ReadAllTextAsync(source);
        graph = IrSerializer.Deserialize(json)
            ?? throw new InvalidOperationException("Could not parse IR file");
    }
    else
    {
        var parser = new CSharpStaticParser();
        Console.Error.WriteLine($"[DCS] Parsing {(commit != null ? $"commit {commit}" : "working directory")}...");
        graph = commit != null
            ? parser.ParseCommit(source, commit)
            : parser.ParseDirectory(source);

        var analyzer = new GraphAnalyzer(graph, rootClassOverride: rootClass);
        analysis = analyzer.Analyze();
    }

    Console.Error.WriteLine($"[DCS] Generating viz for {graph.Nodes.Count} nodes...");
    var html = HtmlVizGenerator.Generate(graph, analysis);

    if (outPath != null)
    {
        await File.WriteAllTextAsync(outPath, html);
        Console.Error.WriteLine($"[DCS] Viz written to {outPath}");
    }
    else
    {
        Console.WriteLine(html);
    }

    return 0;
}

// ── reporting ─────────────────────────────────────────────────────────────────

static void PrintReport(DCS.Core.IR.RegistrationGraph graph, AnalysisResult result)
{
    var w = Console.Out;

    w.WriteLine();
    w.WriteLine("=== DCS Analysis Report ===");
    if (graph.CommitSha != null) w.WriteLine($"Commit: {graph.CommitSha}");
    w.WriteLine($"Nodes: {result.TotalNodes}  Edges: {result.TotalEdges}  BlindSpots: {result.TotalBlindSpots}");
    w.WriteLine();

    WriteSection(w, "LEAKED", result.Leaked.Count);
    foreach (var l in result.Leaked)
        w.WriteLine($"  [ERROR] LEAKED: {l.DisplayName} ({l.FromFramework} → {l.ToFramework}) {FormatLoc(l.SourceFile, l.SourceLine)}");

    WriteSection(w, "BROKEN CHAINS", result.BrokenChains.Count);
    foreach (var b in result.BrokenChains)
        w.WriteLine($"  [ERROR] BROKEN: {b.DisplayName} → {b.MissingDependencyType} not resolved {FormatLoc(b.SourceFile, b.SourceLine)}");

    WriteSection(w, "DUPLICATE REGISTRATIONS", result.Duplicates.Count);
    foreach (var d in result.Duplicates)
        w.WriteLine($"  [WARN]  DUPLICATE: {d.AbstractTokenName} registered {d.NodeIds.Count}× (may indicate leaked migration state)");

    WriteSection(w, "ORPHANED", result.Orphaned.Count);
    foreach (var o in result.Orphaned)
        w.WriteLine($"  [WARN]  ORPHANED: {o.DisplayName} {FormatLoc(o.SourceFile, o.SourceLine)}");

    WriteSection(w, "CYCLES", result.Cycles.Count);
    foreach (var cycle in result.Cycles)
    {
        var names = cycle
            .Select(id => graph.Nodes.FirstOrDefault(n => n.Id == id)?.DisplayName ?? id)
            .ToList();
        w.WriteLine($"  [WARN]  CYCLE: {string.Join(" → ", names)} → {names[0]}");
    }

    WriteSection(w, "BLIND SPOTS", result.TotalBlindSpots);
    foreach (var b in graph.BlindSpots)
        w.WriteLine($"  [WARN]  {b.Pattern.ToUpperInvariant()}: {b.Description} {FormatLoc(b.Location?.FilePath, b.Location?.Line)}");

    w.WriteLine();
    w.WriteLine($"SUMMARY: {(result.HasErrors ? "ERRORS FOUND" : "no errors")} | " +
                $"{result.Leaked.Count} leaked | {result.BrokenChains.Count} broken | " +
                $"{result.Duplicates.Count} duplicate | {result.Orphaned.Count} orphaned | " +
                $"{result.TotalBlindSpots} blind spots");
}

static void PrintDiff(GraphDiff diff)
{
    var w = Console.Out;

    w.WriteLine();
    w.WriteLine("=== DCS Diff Report ===");
    if (diff.OldCommit != null) w.WriteLine($"From: {diff.OldCommit}");
    if (diff.NewCommit != null) w.WriteLine($"To:   {diff.NewCommit}");
    w.WriteLine();

    var added = diff.Added.ToList();
    var removed = diff.Removed.ToList();
    var renamed = diff.Renamed.ToList();
    var modified = diff.Modified.ToList();

    WriteSection(w, "ADDED", added.Count);
    foreach (var c in added)
        w.WriteLine($"  [+] ADDED: {c.NewNode!.DisplayName}");

    WriteSection(w, "REMOVED", removed.Count);
    foreach (var c in removed)
        w.WriteLine($"  [-] REMOVED: {c.OldNode!.DisplayName}");

    WriteSection(w, "RENAMED", renamed.Count);
    foreach (var c in renamed)
        w.WriteLine($"  [~] RENAMED: {c.OldNode!.DisplayName} → {c.NewNode!.DisplayName} (score: {c.SimilarityScore:F2})");

    WriteSection(w, "MODIFIED", modified.Count);
    foreach (var c in modified)
        w.WriteLine($"  [M] {c.Kind.ToString().ToUpperInvariant()}: {c.DisplayName}");

    WriteSection(w, "EDGES ADDED", diff.EdgeChanges.Count(e => e.Kind == EdgeChangeKind.Added));
    WriteSection(w, "EDGES REMOVED", diff.EdgeChanges.Count(e => e.Kind == EdgeChangeKind.Removed));

    w.WriteLine();
    w.WriteLine($"SUMMARY: {added.Count} added | {removed.Count} removed | {renamed.Count} renamed | " +
                $"{modified.Count} modified | " +
                $"{diff.EdgeChanges.Count(e => e.Kind == EdgeChangeKind.Added)} edges+ | " +
                $"{diff.EdgeChanges.Count(e => e.Kind == EdgeChangeKind.Removed)} edges-");

    if (diff.HasBreakingChanges)
        w.WriteLine("NOTE: breaking changes detected (removed nodes/edges)");
}

// ── helpers ───────────────────────────────────────────────────────────────────

static async Task WriteIr(DCS.Core.IR.RegistrationGraph graph, string path)
{
    await IrSerializer.WriteToFileAsync(graph, path);
    Console.Error.WriteLine($"[DCS] IR written to {path}");
}

static (string? repoPath, string? commit, string? irOut, string? rootClass) ParseAnalyzeArgs(string[] args)
{
    string? repoPath = null, commit = null, irOut = null, rootClass = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--commit" or "-c" when i + 1 < args.Length:
                commit = args[++i];
                break;
            case "--ir-out" when i + 1 < args.Length:
                irOut = args[++i];
                break;
            case "--root" when i + 1 < args.Length:
                rootClass = args[++i];
                break;
            default:
                if (!args[i].StartsWith('-') && repoPath == null)
                    repoPath = args[i];
                break;
        }
    }

    return (repoPath, commit, irOut, rootClass);
}

static void PrintHelp()
{
    Console.WriteLine("""
        dcs -- Dependency Chain Substrate CLI

        COMMANDS
          analyze <repo-path> [options]           Extract and analyze for leakage
          dump-ir <repo-path> [options]           Extract IR as JSON (no analysis)
          diff    <repo-path> --from <sha> --to <sha> [options]
                                                  Diff two commits
          viz     <source> [options]              Generate self-contained HTML visualization

        ANALYZE / DUMP-IR OPTIONS
          --commit <sha>        Analyze specific git commit (blob reading, no checkout)
          --ir-out <path>       Write IR JSON to file
          --root <ClassName>    Override composition root detection

        DIFF OPTIONS
          --from <sha>          Base commit SHA
          --to <sha>            Target commit SHA
          --ir-out <path>       Write diff JSON to file

        VIZ OPTIONS
          <repo-path>           Extract from repo (also runs analysis)
          --commit <sha>        Extract specific commit
          --ir <ir-file>        Read from existing IR JSON instead of a repo
          --out <path>          Write HTML to file (default: stdout)

        EXIT CODES
          0   Success / no breaking changes
          1   Errors found / breaking changes in diff
          2   Usage error

        EXAMPLES
          dcs analyze /path/to/repo --commit abc1234
          dcs diff /path/to/repo --from abc1234 --to def5678
          dcs viz /path/to/repo --out graph.html
          dcs viz graph.json --ir --out graph.html
        """);
}

static int ErrorExit(string message)
{
    Console.Error.WriteLine($"[DCS] Error: {message}");
    return 2;
}

static void WriteSection(TextWriter w, string title, int count) =>
    w.WriteLine($"--- {title} ({count}) ---");

static string FormatLoc(string? file, int? line) =>
    file == null ? string.Empty :
    line == null ? $"[{file}]" :
    $"[{file}:{line}]";
