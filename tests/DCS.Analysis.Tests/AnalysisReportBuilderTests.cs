using DCS.Analysis;
using DCS.Core.IR;
using Xunit;

namespace DCS.Analysis.Tests;

public sealed class AnalysisReportBuilderTests
{
    private static RegistrationNode MakeNode(string shortName, string file = "Test.cs", int line = 1)
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId("test-scope", file, line, 0, line, 80, 0);
        return new RegistrationNode
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            DisplayName = shortName,
            AbstractToken = TypeRef.FromShortName(shortName),
            CompositionScopeId = "test-scope",
            SourceLocation = new SourceRef { FilePath = file, Line = line }
        };
    }

    [Fact]
    public void Duplicate_finding_lists_all_sites_with_file_line()
    {
        var nodeA = MakeNode("IFoo", "A.cs", 10);
        var nodeB = MakeNode("IFoo", "B.cs", 20);
        var graph = new RegistrationGraph
        {
            Nodes = [nodeA, nodeB],
            Edges = []
        };

        var analysis = new AnalysisResult
        {
            Duplicates =
            [
                new DuplicateAbstractToken("IFoo", [nodeA.Id, nodeB.Id], IsStrict: true)
            ],
            TotalNodes = 2
        };

        var report = AnalysisReportBuilder.Build(graph, analysis);
        var dup = Assert.Single(report.Findings.Where(f => f.Category == FindingCategory.Duplicate));
        Assert.Equal(2, dup.Sites.Count);
        Assert.Contains(dup.Sites, s => s.FilePath == "A.cs" && s.Line == 10);
        Assert.Contains(dup.Sites, s => s.FilePath == "B.cs" && s.Line == 20);
    }

    [Fact]
    public void Actionable_verbosity_hides_informational_blind_spots()
    {
        var graph = new RegistrationGraph
        {
            BlindSpots =
            [
                new BlindSpotReport
                {
                    Pattern = "factory_lambda",
                    Description = "factory",
                    Location = new SourceRef { FilePath = "F.cs", Line = 5 }
                },
                new BlindSpotReport
                {
                    Pattern = "unrecognized_pattern",
                    Description = "unknown",
                    Location = new SourceRef { FilePath = "U.cs", Line = 9 }
                }
            ]
        };

        var analysis = new AnalysisResult { TotalBlindSpots = 1 };
        var report = AnalysisReportBuilder.Build(graph, analysis, new AnalysisReportBuildOptions
        {
            Verbosity = ReportVerbosity.Actionable
        });

        Assert.Single(report.Findings.Where(f => f.Category == FindingCategory.BlindSpot));
        Assert.Equal(FindingTier.Actionable, report.Findings[0].Tier);
    }

    [Fact]
    public void Strict_mode_includes_intentional_tryadd_duplicate()
    {
        var tryAdd = MakeStrictNode("IFoo", "Try.cs", 1) with
        {
            Annotations = new Dictionary<string, string>
            {
                ["conditional"] = "try_add",
                [StrictDuplicateEligibility.AnnotationKey] = "true"
            }
        };
        var explicitAdd = MakeStrictNode("IFoo", "Add.cs", 2);

        var graph = new RegistrationGraph { Nodes = [tryAdd, explicitAdd] };
        var strictAnalysis = new GraphAnalyzer(graph, policy: FindingPolicyOptions.StrictMode).Analyze();
        var strictReport = AnalysisReportBuilder.Build(graph, strictAnalysis, new AnalysisReportBuildOptions
        {
            Policy = FindingPolicyOptions.StrictMode,
            Verbosity = ReportVerbosity.Full
        });
        Assert.Contains(strictReport.Findings, f =>
            f.Category == FindingCategory.Duplicate && f.Tier == FindingTier.Actionable);

        var defaultAnalysis = new GraphAnalyzer(graph, policy: FindingPolicyOptions.Default).Analyze();
        Assert.Empty(defaultAnalysis.Duplicates);
    }

    private static RegistrationNode MakeStrictNode(string shortName, string file, int line)
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId("test-scope", file, line, 0, line, 80, 0);
        var serviceType = ServiceTypeIdentity.FromResolved(new ResolvedTypeIdentity
        {
            AssemblyKey = AssemblyKey.FromProjectScope("test-scope"),
            MetadataName = $"Test.{shortName}"
        });
        return new RegistrationNode
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            ServiceType = serviceType,
            DuplicateGroupKey = RegistrationNode.ComputeDuplicateGroupKey("test-scope", serviceType),
            CompositionScopeId = "test-scope",
            TypeResolutionQuality = TypeResolutionQuality.Resolved,
            RegistrationRecognitionQuality = RegistrationRecognitionQuality.VerifiedMicrosoftDI,
            DisplayName = shortName,
            AbstractToken = TypeRef.FromQualifiedName($"Test.{shortName}"),
            SourceLocation = new SourceRef { FilePath = file, Line = line },
            Annotations = new Dictionary<string, string>
            {
                [StrictDuplicateEligibility.AnnotationKey] = "true"
            }
        };
    }
}
