using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Core.Serialization;
using DCS.Diff;
using DCS.Fix;
using DCS.Viz;

namespace DCS.Cli;

internal static class ProgramCommands
{
    internal static FrameworkBoundaryModel LoadBoundaries(string? frameworksPath)
    {
        try
        {
            return FrameworkBoundaryModel.Create(frameworksPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load --frameworks config: {ex.Message}", ex);
        }
    }

    internal static RegistrationGraph ExtractGraph(CliOptions options)
    {
        var language = RepoLanguageDetector.Resolve(options.RepoPath, options.Language);
        Console.Error.WriteLine($"[DCS] Language: {language.ToString().ToLowerInvariant()}");

        var parser = CliParserFactory.Create(options);
        var parseResult = CliParserFactory.ExtractParseResult(parser, options);
        return CliParserFactory.SelectGraph(parseResult, options);
    }

    internal static async Task<int> RunAnalyze(string[] args)
    {
        var options = CliArgParser.ParseRepoCommand(args);
        if (options.RepoPath == null) return ErrorExit("analyze requires <repo-path>");

        try
        {
            var graph = ExtractGraph(options);
            var boundaries = LoadBoundaries(options.FrameworksPath);

            Console.Error.WriteLine(
                $"[DCS] {graph.Nodes.Count} registrations, {graph.Edges.Count} edges, {graph.BlindSpots.Count} blind spots");

            var analyzer = new GraphAnalyzer(graph, boundaries, options.RootClass);
            var result = analyzer.Analyze();

            PrintReport(graph, result);

            if (options.IrOut != null)
                await WriteIr(graph, options.IrOut);

            return result.HasErrors ? 1 : 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
        }
    }

    internal static async Task<int> RunAtlas(string[] args)
    {
        var options = CliArgParser.ParseRepoCommand(args, allowRoot: false);
        if (options.RepoPath == null) return ErrorExit("atlas requires <repo-path>");

        try
        {
            var graph = ExtractGraph(options);
            AtlasReporter.Print(graph, Console.Out);

            if (options.IrOut != null)
                await WriteIr(graph, options.IrOut);

            return 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
        }
    }

    internal static async Task<int> RunDumpIr(string[] args)
    {
        var options = CliArgParser.ParseRepoCommand(args, allowRoot: false);
        if (options.RepoPath == null) return ErrorExit("dump-ir requires <repo-path>");

        try
        {
            var graph = ExtractGraph(options);
            var json = IrSerializer.Serialize(graph);

            if (options.IrOut != null)
            {
                await File.WriteAllTextAsync(options.IrOut, json);
                Console.Error.WriteLine($"[DCS] IR written to {options.IrOut}");
            }
            else
            {
                Console.WriteLine(json);
            }

            return 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
        }
    }

    internal static async Task<int> RunDiff(string[] args)
    {
        var options = CliArgParser.ParseDiffCommand(args);
        if (options.RepoPath == null) return ErrorExit("diff requires <repo-path>");
        if (options.FromSha == null) return ErrorExit("diff requires --from <sha>");
        if (options.ToSha == null) return ErrorExit("diff requires --to <sha>");

        try
        {
            var parser = CliParserFactory.Create(options);

            Console.Error.WriteLine($"[DCS] Extracting {options.FromSha[..Math.Min(8, options.FromSha.Length)]}...");
            var oldGraph = CliParserFactory.SelectGraph(parser.ParseCommit(options.RepoPath, options.FromSha), options);
            Console.Error.WriteLine($"[DCS] Extracting {options.ToSha[..Math.Min(8, options.ToSha.Length)]}...");
            var newGraph = CliParserFactory.SelectGraph(parser.ParseCommit(options.RepoPath, options.ToSha), options);

            var diff = new GraphDiffer().Diff(oldGraph, newGraph);
            PrintDiff(diff);

            if (options.IrOut != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(diff, IrSerializer.Options);
                await File.WriteAllTextAsync(options.IrOut, json);
                Console.Error.WriteLine($"[DCS] Diff written to {options.IrOut}");
            }

            return diff.HasBreakingChanges ? 1 : 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
        }
    }

    internal static Task<int> RunFix(string[] args)
    {
        var options = CliArgParser.ParseFixCommand(args);
        if (options.RepoPath == null) return Task.FromResult(ErrorExit("fix requires <repo-path>"));

        if (RepoLanguageDetector.Resolve(options.RepoPath, options.Language) != RepoLanguage.CSharp)
            return Task.FromResult(ErrorExit("fix currently supports C# DI registrations only."));

        if (options.Commit != null)
            return Task.FromResult(ErrorExit("fix operates on the working directory; omit --commit."));

        try
        {
            var graph = ExtractGraph(options);
            var analysis = new GraphAnalyzer(graph, LoadBoundaries(options.FrameworksPath), options.RootClass).Analyze();

            if (analysis.Duplicates.Count == 0)
            {
                Console.Error.WriteLine("[DCS] No duplicate registrations found.");
                return Task.FromResult(0);
            }

            var tokenFilter = options.FixAllDuplicates ? null : options.FixToken ?? analysis.Duplicates[0].AbstractTokenName;
            FixResult result;

            if (options.ApplyFix)
            {
                result = FixEngine.ApplyDuplicateFixes(
                    options.RepoPath,
                    graph,
                    analysis,
                    tokenFilter,
                    options.ForceFix);
                Console.Error.WriteLine($"[DCS] Applied {result.Proposals.Count} duplicate fix(es).");
            }
            else
            {
                result = FixEngine.BuildDuplicateFixes(options.RepoPath, graph, analysis, tokenFilter);
            }

            Console.WriteLine(FixEngine.FormatPreview(result));
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorExit(ex.Message));
        }
    }

    internal static async Task<int> RunViz(string[] args)
    {
        var options = CliArgParser.ParseVizCommand(args);
        if (options.RepoPath == null) return ErrorExit("viz requires <repo-path> or <ir-file> --ir");

        try
        {
            RegistrationGraph graph;
            AnalysisResult? analysis = null;

            if (options.FromIr)
            {
                var json = await File.ReadAllTextAsync(options.RepoPath);
                graph = IrSerializer.Deserialize(json)
                    ?? throw new InvalidOperationException("Could not parse IR file");
            }
            else
            {
                graph = ExtractGraph(options);

                var boundaries = LoadBoundaries(options.FrameworksPath);
                var analyzer = new GraphAnalyzer(graph, boundaries, options.RootClass);
                analysis = analyzer.Analyze();
            }

            Console.Error.WriteLine($"[DCS] Generating viz for {graph.Nodes.Count} nodes...");
            var html = HtmlVizGenerator.Generate(graph, analysis);

            if (options.OutPath != null)
            {
                await File.WriteAllTextAsync(options.OutPath, html);
                Console.Error.WriteLine($"[DCS] Viz written to {options.OutPath}");
            }
            else
            {
                Console.WriteLine(html);
            }

            return 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
        }
    }

    internal static void PrintHelp()
    {
        Console.WriteLine("""
            dcs -- Dependency Chain Substrate CLI

            COMMANDS
              analyze <repo-path> [options]           Extract and analyze for leakage
              atlas   <repo-path> [options]           Human-readable registration listing
              dump-ir <repo-path> [options]           Extract IR as JSON (no analysis)
              diff    <repo-path> --from <sha> --to <sha> [options]
                                                      Diff two commits
              fix     <repo-path> [options]           Preview/apply DUPLICATE registration removal
              viz     <source> [options]              Generate self-contained HTML visualization

            SHARED REPO OPTIONS
              --commit <sha>        Analyze specific git commit (blob reading, no checkout)
              --language <lang>     auto | csharp | java (default: auto-detect from repo)
              --context <id>        Select application context when multiple graphs are returned
              --context-root <fqn>  Limit Spring context discovery to a specific entry root FQN
              --frameworks <path>   Additive custom framework boundary JSON config
              --cache-dir <path>    Override default extraction cache directory
              --no-cache            Bypass extraction cache for this run
              --ir-out <path>       Write IR JSON to file

            ANALYZE OPTIONS
              --root <ClassName>    Override composition root detection

            DIFF OPTIONS
              --from <sha>          Base commit SHA
              --to <sha>            Target commit SHA

            FIX OPTIONS (working directory only; C# repos)
              --preview             Show unified diff without writing (default)
              --apply               Write patched files (requires clean git tree)
              --force               Apply even when git working tree is dirty
              --token <name>        Fix a specific duplicate abstract token
              --all-duplicates      Fix every duplicate group in one run

            VIZ OPTIONS
              <repo-path>           Extract from repo (also runs analysis)
              --commit <sha>        Extract specific commit
              --ir <ir-file>        Read from existing IR JSON instead of a repo
              --out <path>          Write HTML to file (default: stdout)
              --root <ClassName>    Override composition root detection

            EXIT CODES
              0   Success / no breaking changes (atlas always 0)
              1   Errors found / breaking changes in diff
              2   Usage error

            EXAMPLES
              dcs fix /path/to/repo --preview --token IVoiceCloneConsentCoordinator
              dcs fix /path/to/repo --apply --force
              dcs analyze /path/to/repo --commit abc1234
              dcs atlas /path/to/repo --commit abc1234
              dcs diff /path/to/repo --from abc1234 --to def5678 --frameworks fw.json
              dcs viz /path/to/repo --out graph.html
              dcs viz graph.json --ir --out graph.html
            """);
    }

    private static void PrintReport(RegistrationGraph graph, AnalysisResult result)
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

    private static void PrintDiff(GraphDiff diff)
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

    private static async Task WriteIr(RegistrationGraph graph, string path)
    {
        await IrSerializer.WriteToFileAsync(graph, path);
        Console.Error.WriteLine($"[DCS] IR written to {path}");
    }

    private static int ErrorExit(string message)
    {
        Console.Error.WriteLine($"[DCS] Error: {message}");
        return 2;
    }

    private static void WriteSection(TextWriter w, string title, int count) =>
        w.WriteLine($"--- {title} ({count}) ---");

    private static string FormatLoc(string? file, int? line) =>
        file == null ? string.Empty :
        line == null ? $"[{file}]" :
        $"[{file}:{line}]";
}
