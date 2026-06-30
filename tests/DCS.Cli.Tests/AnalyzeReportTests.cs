using DCS.Analysis;
using DCS.Cli;
using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Parser.CSharp;
using Xunit;

namespace DCS.Cli.Tests;

public sealed class AnalyzeReportTests
{
    private static string FixturePath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "fixtures", "di-patterns"));

    [Fact]
    public void Analyze_fixture_produces_json_report_with_schema_version()
    {
        var parseResult = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(FixturePath);
        Assert.True(Directory.Exists(FixturePath), $"Fixture path missing: {FixturePath}");
        var graph = parseResult.ContextGraphs.First(c => c.Graph.Nodes.Count > 0).Graph;

        var analysis = new GraphAnalyzer(graph).Analyze();
        var report = AnalysisReportBuilder.Build(graph, analysis, new AnalysisReportBuildOptions
        {
            Verbosity = ReportVerbosity.Full,
            IncludeMetrics = true
        });

        Assert.Equal("1.0", report.SchemaVersion);
        Assert.NotEmpty(report.Findings);
        var json = AnalysisReportSerializer.Serialize(report);
        Assert.Contains("\"schema_version\": \"1.0\"", json);
        Assert.Contains("\"finding_id\"", json);
    }

    [Fact]
    public void Analyze_fixture_text_report_lists_sites_with_file_line()
    {
        var parseResult = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(FixturePath);
        var graph = parseResult.ContextGraphs.First(c => c.Graph.Nodes.Count > 0).Graph;

        var analysis = new GraphAnalyzer(graph).Analyze();
        var report = AnalysisReportBuilder.Build(graph, analysis);

        using var writer = new StringWriter();
        AnalysisReportPrinter.Print(report, writer, ReportVerbosity.Actionable, verboseBlindSpots: false);
        var text = writer.ToString();

        Assert.Contains("dipatternregistrations.cs:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from unresolved", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Report_out_text_format_writes_human_readable_file()
    {
        var report = BuildFixtureReport();
        var path = Path.Combine(Path.GetTempPath(), $"dcs-report-text-{Guid.NewGuid():N}.txt");

        try
        {
            await AnalysisReportPrinter.WriteToFileAsync(
                report, path, ReportVerbosity.Actionable, verboseBlindSpots: false);

            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("=== DCS Analysis Report ===", text);
            Assert.Contains("dipatternregistrations.cs:", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"schema_version\"", text);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Report_out_json_format_writes_schema_json()
    {
        var report = BuildFixtureReport();
        var path = Path.Combine(Path.GetTempPath(), $"dcs-report-json-{Guid.NewGuid():N}.json");

        try
        {
            await AnalysisReportSerializer.WriteToFileAsync(report, path);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"schema_version\": \"1.0\"", json);
            Assert.Contains("\"finding_id\"", json);
            Assert.DoesNotContain("=== DCS Analysis Report ===", json);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Parse_analyze_args_accepts_text_out_flag()
    {
        var options = CliArgParser.ParseRepoCommand([
            "/tmp/repo",
            "--format", "json",
            "--report-out", "report.json",
            "--text-out", "report.txt"
        ]);

        Assert.Equal(OutputFormat.Json, options.Format);
        Assert.Equal("report.json", options.ReportOut);
        Assert.Equal("report.txt", options.TextOut);
    }

    private static AnalysisReport BuildFixtureReport()
    {
        var parseResult = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(FixturePath);
        var graph = parseResult.ContextGraphs.First(c => c.Graph.Nodes.Count > 0).Graph;
        var analysis = new GraphAnalyzer(graph).Analyze();
        return AnalysisReportBuilder.Build(graph, analysis);
    }
}
