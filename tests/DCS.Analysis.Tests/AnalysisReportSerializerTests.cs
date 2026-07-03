namespace DCS.Analysis.Tests;

public sealed class AnalysisReportSerializerTests
{
    [Fact]
    public void Serialize_uses_snake_case_property_names()
    {
        var report = new AnalysisReport { CommitSha = "abc123" };
        var json = AnalysisReportSerializer.Serialize(report);
        Assert.Contains("\"commit_sha\"", json);
        Assert.Contains("\"schema_version\"", json);
    }

    [Fact]
    public void Serialize_omits_null_optional_fields()
    {
        var report = new AnalysisReport();
        var json = AnalysisReportSerializer.Serialize(report);
        Assert.DoesNotContain("\"commit_sha\"", json);
        Assert.DoesNotContain("\"context_id\"", json);
        Assert.DoesNotContain("\"metrics\"", json);
    }

    [Fact]
    public void Serialize_includes_metrics_when_present()
    {
        var report = new AnalysisReport
        {
            Metrics = new ExtractionQualityMetrics { TotalRegistrations = 10, ResolvedRegistrations = 8 }
        };
        var json = AnalysisReportSerializer.Serialize(report);
        Assert.Contains("\"total_registrations\": 10", json);
    }

    [Fact]
    public void Serialize_encodes_finding_category_and_severity_as_snake_case_strings()
    {
        var report = new AnalysisReport
        {
            Findings =
            [
                new AnalysisFinding
                {
                    FindingId = "f1",
                    Category = FindingCategory.PossibleDuplicate,
                    Severity = FindingSeverity.Warn,
                    Tier = FindingTier.ParserLimit,
                    Title = "Possible duplicate"
                }
            ]
        };

        var json = AnalysisReportSerializer.Serialize(report);
        Assert.Contains("\"possible_duplicate\"", json);
        Assert.Contains("\"warn\"", json);
        Assert.Contains("\"parser_limit\"", json);
    }

    [Fact]
    public void Serialize_empty_findings_produces_empty_array()
    {
        var report = new AnalysisReport { Findings = [] };
        var json = AnalysisReportSerializer.Serialize(report);
        Assert.Contains("\"findings\": []", json);
    }

    [Fact]
    public void Serialize_multi_context_report_nests_context_reports()
    {
        var multi = new MultiContextAnalysisReport
        {
            CommitSha = "deadbeef",
            ContextReports =
            [
                new AnalysisReport { ContextId = "net8.0" },
                new AnalysisReport { ContextId = "net10.0" }
            ]
        };

        var json = AnalysisReportSerializer.Serialize(multi);
        Assert.Contains("\"deadbeef\"", json);
        Assert.Contains("\"net8.0\"", json);
        Assert.Contains("\"net10.0\"", json);
    }

    [Fact]
    public async Task WriteToFileAsync_single_report_writes_readable_file()
    {
        var report = new AnalysisReport { CommitSha = "sha1" };
        var path = Path.Combine(Path.GetTempPath(), $"dcs-report-{Guid.NewGuid():N}.json");

        try
        {
            await AnalysisReportSerializer.WriteToFileAsync(report, path);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"sha1\"", content);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteToFileAsync_multi_context_report_writes_readable_file()
    {
        var multi = new MultiContextAnalysisReport { CommitSha = "sha2" };
        var path = Path.Combine(Path.GetTempPath(), $"dcs-multi-report-{Guid.NewGuid():N}.json");

        try
        {
            await AnalysisReportSerializer.WriteToFileAsync(multi, path);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"sha2\"", content);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
