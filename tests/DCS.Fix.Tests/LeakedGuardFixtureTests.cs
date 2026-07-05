using DCS.Analysis;
using DCS.Core.Parsing;
using DCS.Fix;
using DCS.Parser.CSharp;

namespace DCS.Fix.Tests;

/// <summary>
/// Contract tests for the LEAKED guard codemod fixture (ADR-007 amendment).
/// </summary>
public sealed class LeakedGuardFixtureTests
{
    private static string FixturePath =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "di-patterns-leaked-guard"));

    private static string GoldenDiffPath =>
        Path.Combine(FixturePath, "expected", "leaked-guard-preview.diff");

    [Fact]
    public void Fixture_produces_cross_shell_leaked_finding()
    {
        Assert.True(Directory.Exists(FixturePath), $"Fixture path missing: {FixturePath}");

        var graph = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(FixturePath)
            .SingleGraphOrDefault()!;

        var analysis = new GraphAnalyzer(graph).Analyze();

        Assert.NotEmpty(analysis.Leaked);
        var leaked = Assert.Single(analysis.Leaked, l =>
            string.Equals(l.DisplayName, "ISharedFeature", StringComparison.Ordinal));
        var frameworkTags = $"{leaked.FromFramework}|{leaked.ToFramework}";
        Assert.Contains("winui", frameworkTags, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avalonia", frameworkTags, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Golden_preview_diff_documents_winui_guard_contract()
    {
        Assert.True(File.Exists(GoldenDiffPath), $"Golden diff missing: {GoldenDiffPath}");

        var golden = File.ReadAllText(GoldenDiffPath);
        Assert.Contains("#if WINUI", golden, StringComparison.Ordinal);
        Assert.Contains("#endif", golden, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISharedFeature, WinuiFeature>();", golden, StringComparison.Ordinal);
        Assert.Contains("winuishellregistrations.cs", golden, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_matches_golden_diff()
    {
        var graph = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(FixturePath)
            .SingleGraphOrDefault()!;
        var analysis = new GraphAnalyzer(graph).Analyze();

        var result = FixEngine.BuildLeakedFixes(FixturePath, graph, analysis, "ISharedFeature");
        var patch = Assert.Single(
            result.Patches,
            p => p.RelativePath.Contains("WinuiShellRegistrations", StringComparison.OrdinalIgnoreCase));

        var actual = UnifiedDiffFormatter.Format(patch.RelativePath, patch.OriginalContent, patch.UpdatedContent);
        Assert.Equal(
            NormalizeLineEndings(File.ReadAllText(GoldenDiffPath)),
            NormalizeLineEndings(actual));
    }

    [Fact]
    public void Plan_proposes_winui_guard_and_skips_composition_root_peer()
    {
        var graph = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(FixturePath)
            .SingleGraphOrDefault()!;
        var analysis = new GraphAnalyzer(graph).Analyze();

        var proposals = LeakedFixPlanner.Plan(FixturePath, graph, analysis, "ISharedFeature");

        var proposal = Assert.Single(proposals);
        Assert.Equal("WINUI", proposal.GuardSymbol);
        Assert.Contains("WinuiShellRegistrations", proposal.RelativeFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_writes_guard_passes_graph_guards_and_build_verification()
    {
        var root = CopyFixtureToTemp();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var before = new GraphAnalyzer(graph).Analyze();

            var result = FixEngine.ApplyLeakedFixes(root, graph, before, "ISharedFeature", forceDirtyTree: true);
            Assert.True(result.Applied);

            var winuiPath = Directory.GetFiles(root, "*WinuiShellRegistrations.cs", SearchOption.AllDirectories).Single();
            var contents = File.ReadAllText(winuiPath);
            Assert.Contains("#if WINUI", contents, StringComparison.Ordinal);
            Assert.Contains("#endif", contents, StringComparison.Ordinal);

            var afterGraph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var after = new GraphAnalyzer(afterGraph).Analyze();
            FixSafetyGuard.VerifyApplyGuards(before, after, root, result.Patches);
            FixBuildVerifier.VerifyOrRollback(root, result.Patches, FixBuildVerifier.RunDotnetBuild);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_rolls_back_when_build_verification_fails()
    {
        var root = CopyFixtureToTemp();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var before = new GraphAnalyzer(graph).Analyze();

            var result = FixEngine.ApplyLeakedFixes(root, graph, before, "ISharedFeature", forceDirtyTree: true);
            var winuiPath = Directory.GetFiles(root, "*WinuiShellRegistrations.cs", SearchOption.AllDirectories).Single();
            var patch = Assert.Single(result.Patches);
            Assert.Contains("#if WINUI", File.ReadAllText(winuiPath), StringComparison.Ordinal);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                FixBuildVerifier.VerifyOrRollback(
                    root,
                    result.Patches,
                    _ => new BuildVerificationResult(false, 1, "compile failed")));

            Assert.Contains("Fix rolled back", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(patch.OriginalContent, File.ReadAllText(winuiPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CopyFixtureToTemp()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"dcs-leaked-apply-{Guid.NewGuid():N}");
        CopyDirectory(FixturePath, dest);
        return dest;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd() + "\n";
}
