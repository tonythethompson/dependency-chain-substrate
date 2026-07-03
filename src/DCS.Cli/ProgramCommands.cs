using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Core.Serialization;
using DCS.Diff;
using DCS.Fix;
using DCS.Runtime;
using DCS.Viz;
using System.Diagnostics;
using System.Text.Json;

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

    internal static RegistrationGraph ExtractGraph(CliOptions options, out ParseResult parseResult)
    {
        options = CliParserFactory.ResolveExtractionOptions(options);
        var language = RepoLanguageDetector.Resolve(options.RepoPath, options.Language);
        Console.Error.WriteLine($"[DCS] Language: {language.ToString().ToLowerInvariant()}");

        if (!string.IsNullOrWhiteSpace(options.TargetFramework))
            Console.Error.WriteLine($"[DCS] Target framework: {options.TargetFramework}");

        var parser = CliParserFactory.Create(options);
        parseResult = CliParserFactory.ExtractParseResult(parser, options);

        if (parseResult.ContextGraphs.Count > 1)
        {
            Console.Error.WriteLine(
                $"[DCS] Contexts available: {string.Join(", ", parseResult.ContextGraphs.Select(c => c.ContextId))}");
        }

        return CliParserFactory.SelectGraph(parseResult, options);
    }

    internal static RegistrationGraph ExtractGraph(CliOptions options)
    {
        return ExtractGraph(options, out _);
    }

    internal static async Task<int> RunAnalyze(string[] args)
    {
        var options = CliArgParser.ParseRepoCommand(args);
        if (options.RepoPath == null) return ErrorExit("analyze requires <repo-path>");

        try
        {
            options = CliParserFactory.ResolveExtractionOptions(options);
            var boundaries = LoadBoundaries(options.FrameworksPath);
            var policy = options.Strict ? FindingPolicyOptions.StrictMode : FindingPolicyOptions.Default;
            var includeMetrics = options.Metrics || options.Verbosity == ReportVerbosity.Full;

            if (options.ContextAll)
            {
                var parser = CliParserFactory.Create(options);
                var parseResult = CliParserFactory.ExtractParseResult(parser, options);
                PrintContextBanner(parseResult);

                var contextReports = new List<AnalysisReport>();
                var hasErrors = false;

                foreach (var ctx in parseResult.ContextGraphs)
                {
                    var ctxGraph = ctx.Graph;
                    Console.Error.WriteLine(
                        $"[DCS] Context {ctx.ContextId}: {ctxGraph.Nodes.Count} registrations, {ctxGraph.Edges.Count} edges, {ctxGraph.BlindSpots.Count} blind spots");

                    var result = new GraphAnalyzer(ctxGraph, boundaries, options.RootClass, policy).Analyze();
                    var report = BuildReport(ctxGraph, result, options, policy, includeMetrics, ctx.ContextId, parseResult);
                    contextReports.Add(report);
                    if (result.HasErrors) hasErrors = true;
                }

                var multi = new MultiContextAnalysisReport
                {
                    CommitSha = parseResult.ContextGraphs.FirstOrDefault()?.Graph.CommitSha,
                    ContextReports = contextReports
                };

                await EmitAnalyzeOutput(multi, options);
                if (options.IrOut != null)
                    await WriteParseResult(parseResult, options.IrOut);

                return hasErrors ? 1 : 0;
            }

            var graph = ExtractGraph(options, out var singleParse);
            PrintContextBanner(singleParse);
            Console.Error.WriteLine(
                $"[DCS] {graph.Nodes.Count} registrations, {graph.Edges.Count} edges, {graph.BlindSpots.Count} blind spots");

            var analysisResult = new GraphAnalyzer(graph, boundaries, options.RootClass, policy).Analyze();
            var singleReport = BuildReport(
                graph,
                analysisResult,
                options,
                policy,
                includeMetrics,
                singleParse.ContextGraphs.FirstOrDefault(c => c.Graph == graph)?.ContextId ??
                singleParse.ContextGraphs.FirstOrDefault()?.ContextId,
                singleParse);

            await EmitAnalyzeOutput(singleReport, options);

            if (options.IrOut != null)
                await WriteIr(graph, options.IrOut);

            return analysisResult.HasErrors ? 1 : 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
        }
    }

    private static AnalysisReport BuildReport(
        RegistrationGraph graph,
        AnalysisResult result,
        CliOptions options,
        FindingPolicyOptions policy,
        bool includeMetrics,
        string? contextId,
        ParseResult parseResult) =>
        AnalysisReportBuilder.Build(graph, result, new AnalysisReportBuildOptions
        {
            Policy = policy,
            Verbosity = options.Verbosity,
            VerboseBlindSpots = options.VerboseBlindSpots || options.Verbosity == ReportVerbosity.Full,
            IncludeMetrics = includeMetrics,
            ContextId = contextId,
            TargetFramework = options.TargetFramework,
            ParserVersion = graph.ParserVersion,
            AvailableContexts = parseResult.ContextGraphs.Select(c => c.ContextId).ToList()
        });

    private static async Task EmitAnalyzeOutput(AnalysisReport report, CliOptions options)
    {
        if (options.Format == OutputFormat.Json)
            Console.WriteLine(AnalysisReportSerializer.Serialize(report));
        else
        {
            AnalysisReportPrinter.Print(report, Console.Out, options.Verbosity, options.VerboseBlindSpots);
            if (report.Metrics != null)
                AnalysisReportPrinter.PrintMetrics(report.Metrics, Console.Error);
        }

        await WriteAnalyzeReportFilesAsync(report, options);
    }

    private static async Task EmitAnalyzeOutput(MultiContextAnalysisReport multi, CliOptions options)
    {
        if (options.Format == OutputFormat.Json)
            Console.WriteLine(AnalysisReportSerializer.Serialize(multi));
        else
            AnalysisReportPrinter.PrintMultiContext(multi, Console.Out, options.Verbosity);

        await WriteAnalyzeReportFilesAsync(multi, options);
    }

    private static async Task WriteAnalyzeReportFilesAsync(AnalysisReport report, CliOptions options)
    {
        if (options.ReportOut != null)
        {
            if (options.Format == OutputFormat.Json)
                await AnalysisReportSerializer.WriteToFileAsync(report, options.ReportOut);
            else
                await AnalysisReportPrinter.WriteToFileAsync(
                    report, options.ReportOut, options.Verbosity, options.VerboseBlindSpots);
        }

        if (options.TextOut != null &&
            !string.Equals(options.TextOut, options.ReportOut, StringComparison.OrdinalIgnoreCase))
        {
            await AnalysisReportPrinter.WriteToFileAsync(
                report, options.TextOut, options.Verbosity, options.VerboseBlindSpots);
        }
    }

    private static async Task WriteAnalyzeReportFilesAsync(MultiContextAnalysisReport multi, CliOptions options)
    {
        if (options.ReportOut != null)
        {
            if (options.Format == OutputFormat.Json)
                await AnalysisReportSerializer.WriteToFileAsync(multi, options.ReportOut);
            else
                await AnalysisReportPrinter.WriteToFileAsync(multi, options.ReportOut, options.Verbosity);
        }

        if (options.TextOut != null &&
            !string.Equals(options.TextOut, options.ReportOut, StringComparison.OrdinalIgnoreCase))
        {
            await AnalysisReportPrinter.WriteToFileAsync(multi, options.TextOut, options.Verbosity);
        }
    }

    private static void PrintContextBanner(ParseResult parseResult)
    {
        if (parseResult.ContextGraphs.Count <= 1)
            return;

        Console.Error.WriteLine(
            $"[DCS] Contexts available: {string.Join(", ", parseResult.ContextGraphs.Select(c => c.ContextId))}");
    }

    internal static async Task<int> RunPath(string[] args)
    {
        var options = CliArgParser.ParsePathCommand(args);
        if (options.RepoPath == null) return ErrorExit("path requires <repo-path>");
        if (string.IsNullOrWhiteSpace(options.PathTo))
            return ErrorExit("path requires --to <registration>");

        try
        {
            var graph = ExtractGraph(options, out var parseResult);
            PrintContextBanner(parseResult);
            Console.Error.WriteLine(
                $"[DCS] Path query on {graph.Nodes.Count} registrations, {graph.Edges.Count} edges");

            var pathResult = GraphPathFinder.FindPath(
                graph, options.PathFrom, options.PathTo!, options.RootClass);

            if (pathResult.IsAmbiguous)
                return ErrorExit(pathResult.Error ?? "Ambiguous path query.");

            if (!pathResult.Success)
                return ErrorExit(pathResult.Error ?? "No path found.");

            var report = PathExcavationReport.FromResult(pathResult);
            if (options.Format == OutputFormat.Json)
            {
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                if (options.ReportOut != null)
                    await File.WriteAllTextAsync(options.ReportOut, json);
                else
                    Console.WriteLine(json);
            }
            else
            {
                if (options.ReportOut != null)
                {
                    await using var writer = new StreamWriter(options.ReportOut);
                    PathExcavationPrinter.Print(pathResult, writer);
                }
                else
                {
                    PathExcavationPrinter.Print(pathResult, Console.Out);
                }
            }

            if (options.IrOut != null)
                await WriteIr(graph, options.IrOut);

            return 0;
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

            if (options.FixClass == FixClass.Broken)
            {
                var measurement = BrokenFixMeasurement.Measure(options.RepoPath!, graph, analysis);
                Console.Error.WriteLine(
                    $"[DCS] Broken measurement: total={measurement.TotalBroken}, eligible={measurement.EligibleForFixPreview}");

                if (measurement.EligibleForFixPreview == 0)
                {
                    Console.WriteLine("No eligible broken-chain fixes available.");
                    Console.WriteLine(measurement.FormatSummary());
                    return Task.FromResult(0);
                }

                var brokenTargetFilter = options.FixToken;
                BrokenFixResult brokenResult;

                if (options.ApplyFix)
                {
                    brokenResult = FixEngine.ApplyBrokenFixes(
                        options.RepoPath!,
                        graph,
                        analysis,
                        brokenTargetFilter,
                        options.ForceFix);
                    VerifyFixGuardsAfterApply(options, analysis, brokenResult.Patches);
                    if (options.VerifyBuild)
                        VerifyBuildAfterApply(options.RepoPath!, brokenResult.Patches, RunDotnetBuild);
                    Console.Error.WriteLine($"[DCS] Applied {brokenResult.Proposals.Count} broken fix(es).");
                }
                else
                {
                    brokenResult = FixEngine.BuildBrokenFixes(
                        options.RepoPath!, graph, analysis, brokenTargetFilter);
                }

                Console.WriteLine(FixEngine.FormatBrokenPreview(brokenResult, measurement));
                return Task.FromResult(0);
            }

            if (options.FixClass == FixClass.Orphaned)
            {
                var measurement = OrphanedFixMeasurement.Measure(graph, analysis);
                Console.Error.WriteLine(
                    $"[DCS] Orphaned measurement: total={measurement.TotalOrphaned}, " +
                    $"explicit_with_site={measurement.ExplicitWithSite}, eligible={measurement.EligibleForFixPreview}");

                if (measurement.EligibleForFixPreview == 0)
                {
                    Console.WriteLine("No eligible orphaned registration fixes available.");
                    Console.WriteLine(measurement.FormatSummary());
                    return Task.FromResult(0);
                }

                var orphanedTokenFilter = options.FixToken;
                OrphanedFixResult orphanedResult;

                if (options.ApplyFix)
                {
                    orphanedResult = FixEngine.ApplyOrphanedFixes(
                        options.RepoPath!,
                        graph,
                        analysis,
                        orphanedTokenFilter,
                        options.ForceFix);
                    VerifyFixGuardsAfterApply(options, analysis, orphanedResult.Patches);
                    if (options.VerifyBuild)
                        VerifyBuildAfterApply(options.RepoPath!, orphanedResult.Patches, RunDotnetBuild);
                    Console.Error.WriteLine($"[DCS] Applied {orphanedResult.Proposals.Count} orphaned fix(es).");
                }
                else
                {
                    orphanedResult = FixEngine.BuildOrphanedFixes(
                        options.RepoPath!, graph, analysis, orphanedTokenFilter);
                }

                Console.WriteLine(FixEngine.FormatOrphanedPreview(orphanedResult, measurement));
                return Task.FromResult(0);
            }

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
                VerifyFixGuardsAfterApply(options, analysis, result.Patches);
                if (options.VerifyBuild)
                    VerifyBuildAfterApply(options.RepoPath!, result.Patches, RunDotnetBuild);
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

    internal static async Task<int> RunEnrich(string[] args)
    {
        var options = CliArgParser.ParseEnrichCommand(args);
        if (options.RepoPath == null) return ErrorExit("enrich requires <ir-file>");
        if (options.RuntimeLogPath == null) return ErrorExit("enrich requires --runtime-log <path>");

        try
        {
            if (!File.Exists(options.RepoPath))
                return ErrorExit($"IR file not found: {options.RepoPath}");
            if (!File.Exists(options.RuntimeLogPath))
                return ErrorExit($"Runtime log not found: {options.RuntimeLogPath}");

            var json = await File.ReadAllTextAsync(options.RepoPath);
            var staticGraph = IrSerializer.Deserialize(json)
                ?? throw new InvalidOperationException("Could not parse IR file");

            var events = RuntimeLogReader.ReadJsonl(options.RuntimeLogPath);
            var analysis = new GraphAnalyzer(staticGraph, LoadBoundaries(options.FrameworksPath), options.RootClass)
                .Analyze();
            var report = RuntimeGraphEnricher.Enrich(staticGraph, events, analysis);

            var enrichedJson = IrSerializer.Serialize(report.EnrichedGraph);
            if (options.OutPath != null)
            {
                await File.WriteAllTextAsync(options.OutPath, enrichedJson);
                Console.Error.WriteLine($"[DCS] Enriched IR written to {options.OutPath}");
            }
            else
            {
                Console.WriteLine(enrichedJson);
            }

            Console.Error.WriteLine(
                $"[DCS] Runtime enrichment: {report.AnnotatedNodeCount}/{staticGraph.Nodes.Count} nodes annotated, " +
                $"{report.TotalResolutionEvents} events, " +
                $"{report.OrphanedReclassifiedNodeIds.Count} orphaned reclassified, " +
                $"{report.BlindSpotConfirmedNodeIds.Count} blind spots confirmed, " +
                $"{report.CaptiveDependencies.Count} captive dependency finding(s)");

            if (report.CaptiveDependencies.Count > 0)
            {
                foreach (var finding in report.CaptiveDependencies)
                {
                    Console.Error.WriteLine(
                        $"[DCS] Captive: scoped {finding.ScopedServiceType} resolved from singleton {finding.CaptiveSingletonType} ({finding.EventCount} event(s))");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return ErrorExit(ex.Message);
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

            VizPathHighlight? pathHighlight = null;
            if (!string.IsNullOrWhiteSpace(options.PathTo))
            {
                var pathResult = GraphPathFinder.FindPath(
                    graph, options.PathFrom, options.PathTo!, options.RootClass);
                if (pathResult.IsAmbiguous)
                    Console.Error.WriteLine($"[DCS] Warning: ambiguous path — {pathResult.Error}");
                else if (!pathResult.Success)
                    Console.Error.WriteLine($"[DCS] Warning: no path found — {pathResult.Error}");
                else
                {
                    pathHighlight = VizPathHighlight.FromResult(pathResult);
                    Console.Error.WriteLine(
                        $"[DCS] Path highlight: {pathResult.Nodes.Count} nodes, {pathResult.Edges.Count} edges");
                }
            }

            var html = HtmlVizGenerator.Generate(graph, analysis, pathHighlight);

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
              fix     <repo-path> [options]           Preview/apply safe C# registration fixes
              path    <repo-path> --to <registration> [options]
                                                      Dependency path from root to target registration
              enrich  <ir-file> --runtime-log <path> [options]
                                                      Merge static IR with runtime resolution log
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
              --target-framework <tfm>
                                    Single TFM graph (e.g. net10.0). Default: portable primary TFM.
              --context <id>        Context id (e.g. csharp|net10.0) or "all" for multi-context summary
              --all-target-frameworks
                                    One graph per TFM; use with --context to select.
              --production-only     Exclude test/benchmark projects (default)
              --include-tests       Include test projects and tests/ sources
              --verbosity <level>   summary | actionable (default) | full
              --strict              Disable finding suppressions (audit mode)
              --verbose-blind-spots List informational blind spots in text output
              --metrics             Print extraction quality metrics on stderr
              --format <fmt>        text (default) | json
              --report-out <path>   Write analysis report file (format follows --format)
              --text-out <path>     Write human-readable report (with --format json)
              --no-cache            Bypass extraction cache (recommended after parser updates)

            PowerShell: quote pipe in context — --context "csharp|net10.0"

            DIFF OPTIONS
              --from <sha>          Base commit SHA
              --to <sha>            Target commit SHA

            FIX OPTIONS (working directory only; C# repos)
              --preview             Show unified diff without writing (default)
              --apply               Write patched files (requires clean git tree)
              --fix-class <kind>    duplicate (default) | orphaned | broken
              --force               Apply even when git working tree is dirty
              --verify-build        Run dotnet build after apply; rollback on failure
              --token <name>        Fix a specific duplicate, orphaned, or broken target
              --all-duplicates      Fix every duplicate group in one run

            VIZ OPTIONS
              <repo-path>           Extract from repo (also runs analysis)
              --commit <sha>        Extract specific commit
              --path-to <token>     Highlight dependency path to registration
              --path-from <token>   Optional path origin (default: composition-root seeds)
              --ir <ir-file>        Read from existing IR JSON instead of a repo
              --out <path>          Write HTML to file (default: stdout)
              --root <ClassName>    Override composition root detection

            ENRICH OPTIONS
              <ir-file>             Static IR JSON from dump-ir or analyze --ir-out
              --runtime-log <path>  JSONL runtime resolution log (required)
              --out <path>          Write enriched IR JSON (default: stdout)
              --frameworks <path>   Custom framework boundary JSON for orphaned reclassification
              --root <ClassName>    Override composition root detection

            EXIT CODES
              0   Success / no breaking changes (atlas always 0)
              1   Errors found / breaking changes in diff
              2   Usage error

            EXAMPLES
              dcs fix /path/to/repo --preview --token IVoiceCloneConsentCoordinator
              dcs fix /path/to/repo --fix-class orphaned --preview
              dcs fix /path/to/repo --fix-class broken --preview --token IDependency
              dcs fix /path/to/repo --apply --force --verify-build
              dcs analyze /path/to/repo --commit abc1234 --format json --report-out report.json
              dcs analyze /path/to/repo --commit abc1234 --format text --report-out report.txt
              dcs analyze /path/to/repo --commit abc1234 --format json --report-out report.json --text-out report.txt
              dcs analyze /path/to/repo --commit abc1234 --context all --verbosity summary
              dcs path /path/to/repo --commit abc1234 --to VoiceCloneConsentCoordinator
              dcs path /path/to/repo --from IApplicationLogger --to IConsentService --format json
              dcs atlas /path/to/repo --commit abc1234
              dcs diff /path/to/repo --from abc1234 --to def5678 --frameworks fw.json
              dcs viz /path/to/repo --out graph.html
              dcs viz /path/to/repo --path-to VoiceCloneConsentCoordinator --out graph.html
              dcs viz graph.json --ir --out graph.html
            """);
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

    private static void VerifyFixGuardsAfterApply(
        CliOptions options,
        AnalysisResult before,
        IReadOnlyList<FilePatch> patches)
    {
        AnalysisResult? after = null;
        FixSafetyGuard.VerifyAfterApplyOrRollback(
            before,
            options.RepoPath!,
            patches,
            () =>
            {
                var afterGraph = ExtractGraph(options);
                after = new GraphAnalyzer(afterGraph, LoadBoundaries(options.FrameworksPath), options.RootClass)
                    .Analyze();
                return after;
            });

        Console.Error.WriteLine(
            $"[DCS] Apply guards: OK (LEAKED {before.Leaked.Count}→{after!.Leaked.Count}, " +
            $"BROKEN {before.BrokenChains.Count}→{after.BrokenChains.Count})");
    }

    internal static void VerifyBuildAfterApply(
        string repoRoot,
        IReadOnlyList<FilePatch> patches,
        Func<string, BuildVerificationResult> buildRunner)
    {
        var result = buildRunner(repoRoot);
        if (result.Succeeded)
        {
            Console.Error.WriteLine("[DCS] Build verification: OK");
            return;
        }

        FixSafetyGuard.RollbackPatches(repoRoot, patches);
        var detail = string.IsNullOrWhiteSpace(result.Output)
            ? $"exit code {result.ExitCode}"
            : result.Output.Trim();
        throw new InvalidOperationException($"Fix rolled back: build verification failed ({detail}).");
    }

    private static BuildVerificationResult RunDotnetBuild(string repoRoot)
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

    private static async Task WriteIr(RegistrationGraph graph, string path)
    {
        await IrSerializer.WriteToFileAsync(graph, path);
        Console.Error.WriteLine($"[DCS] IR written to {path}");
    }

    private static async Task WriteParseResult(ParseResult parseResult, string path)
    {
        await File.WriteAllTextAsync(path, ParseResultSerializer.Serialize(parseResult));
        Console.Error.WriteLine($"[DCS] IR bundle written to {path}");
    }

    private static int ErrorExit(string message)
    {
        Console.Error.WriteLine($"[DCS] Error: {message}");
        return 2;
    }

    private static void WriteSection(TextWriter w, string title, int count) =>
        w.WriteLine($"--- {title} ({count}) ---");
}
