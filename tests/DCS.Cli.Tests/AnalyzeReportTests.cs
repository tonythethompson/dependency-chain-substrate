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
}
