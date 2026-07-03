using DCS.Cli;
using DCS.Core.IR;
using DCS.Core.Parsing;

namespace DCS.Cli.Tests;

public sealed class CliParserFactoryTests
{
    private static readonly TypeRef SomeRoot = TypeRef.FromQualifiedName("App.Program");


    [Fact]
    public void ParseFixClass_rejects_unknown_value()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliArgParser.ParseFixClass("nonsense"));
        Assert.Contains("Unknown fix class", ex.Message);
    }

    [Theory]
    [InlineData("duplicate", "Duplicate")]
    [InlineData("duplicates", "Duplicate")]
    [InlineData("orphaned", "Orphaned")]
    [InlineData("orphan", "Orphaned")]
    [InlineData("broken", "Broken")]
    [InlineData("broken-chain", "Broken")]
    [InlineData("broken-chains", "Broken")]
    [InlineData("DUPLICATE", "Duplicate")]
    public void ParseFixClass_accepts_known_aliases(string value, string expectedName)
    {
        Assert.Equal(expectedName, CliArgParser.ParseFixClass(value).ToString());
    }

    [Fact]
    public void ParseRepoCommand_rejects_unknown_verbosity()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgParser.ParseRepoCommand(["repo", "--verbosity", "bogus"]));
        Assert.Contains("Unknown verbosity", ex.Message);
    }

    [Fact]
    public void ParseRepoCommand_rejects_unknown_format()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgParser.ParseRepoCommand(["repo", "--format", "xml"]));
        Assert.Contains("Unknown format", ex.Message);
    }

    [Fact]
    public void ParseRepoCommand_include_tests_disables_production_only()
    {
        var options = CliArgParser.ParseRepoCommand(["repo", "--include-tests"]);
        Assert.False(options.ProductionOnly);
        Assert.True(options.IncludeTests);
    }

    [Fact]
    public void ParseRepoCommand_production_only_overrides_prior_include_tests()
    {
        var options = CliArgParser.ParseRepoCommand(["repo", "--include-tests", "--production-only"]);
        Assert.True(options.ProductionOnly);
        Assert.False(options.IncludeTests);
    }

    [Fact]
    public void ParseRepoCommand_all_target_frameworks_clears_explicit_target()
    {
        var options = CliArgParser.ParseRepoCommand(
            ["repo", "--target-framework", "net8.0", "--all-target-frameworks"]);

        Assert.True(options.AllTargetFrameworks);
        Assert.Null(options.TargetFramework);
    }

    [Fact]
    public void ParseRepoCommand_target_framework_after_all_clears_all_flag()
    {
        var options = CliArgParser.ParseRepoCommand(
            ["repo", "--all-target-frameworks", "--target-framework", "net10.0"]);

        Assert.False(options.AllTargetFrameworks);
        Assert.Equal("net10.0", options.TargetFramework);
    }

    [Fact]
    public void ParseRepoCommand_context_all_sets_context_all_flag()
    {
        var options = CliArgParser.ParseRepoCommand(["repo", "--context", "all"]);
        Assert.True(options.ContextAll);
        Assert.Null(options.ContextId);
    }

    [Fact]
    public void ParseRepoCommand_dangling_flag_without_value_is_ignored()
    {
        // --commit as last arg has no following value; should not throw, repoPath unaffected
        var options = CliArgParser.ParseRepoCommand(["repo", "--commit"]);
        Assert.Equal("repo", options.RepoPath);
        Assert.Null(options.Commit);
    }

    [Fact]
    public void ParseRepoCommand_first_positional_wins_when_repeated()
    {
        var options = CliArgParser.ParseRepoCommand(["first-repo", "second-repo"]);
        Assert.Equal("first-repo", options.RepoPath);
    }

    [Fact]
    public void ParseFixCommand_defaults_to_csharp_when_language_unset()
    {
        var options = CliArgParser.ParseFixCommand(["repo", "--apply"]);
        Assert.Equal(RepoLanguage.CSharp, options.Language);
        Assert.True(options.ApplyFix);
    }

    [Fact]
    public void ParseFixCommand_preview_after_apply_disables_apply()
    {
        var options = CliArgParser.ParseFixCommand(["repo", "--apply", "--preview"]);
        Assert.False(options.ApplyFix);
    }

    [Fact]
    public void ParseFixCommand_rejects_unknown_fix_class()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgParser.ParseFixCommand(["repo", "--fix-class", "unknown"]));
        Assert.Contains("Unknown fix class", ex.Message);
    }

    [Fact]
    public void ExtractParseResult_throws_when_repo_path_missing()
    {
        var options = new CliOptions();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CliParserFactory.ExtractParseResult(null!, options));
        Assert.Contains("Repository path is required", ex.Message);
    }

    [Fact]
    public void SelectGraph_throws_when_no_context_graphs_present_for_context_all()
    {
        var options = new CliOptions { ContextAll = true };
        var result = new ParseResult { ContextGraphs = [] };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CliParserFactory.SelectGraph(result, options));
        Assert.Contains("No context graphs found", ex.Message);
    }

    [Fact]
    public void SelectGraph_throws_helpful_message_for_incomplete_csharp_context_id()
    {
        var options = new CliOptions { ContextId = "csharp" };
        var result = new ParseResult { ContextGraphs = [] };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CliParserFactory.SelectGraph(result, options));
        Assert.Contains("PowerShell may have split", ex.Message);
    }

    [Fact]
    public void SelectGraph_throws_when_context_id_not_found()
    {
        var options = new CliOptions { ContextId = "missing-context" };
        var graph = new RegistrationGraph();
        var result = new ParseResult
        {
            ContextGraphs = [new ContextGraph { ContextId = "net8.0", EntryRoot = SomeRoot, Graph = graph }]
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CliParserFactory.SelectGraph(result, options));
        Assert.Contains("not found", ex.Message);
        Assert.Contains("net8.0", ex.Message);
    }

    [Fact]
    public void SelectGraph_returns_single_context_graph_when_unambiguous()
    {
        var options = new CliOptions();
        var graph = new RegistrationGraph();
        var result = new ParseResult
        {
            ContextGraphs = [new ContextGraph { ContextId = "net8.0", EntryRoot = SomeRoot, Graph = graph }]
        };

        var selected = CliParserFactory.SelectGraph(result, options);
        Assert.Same(graph, selected);
    }

    [Fact]
    public void SelectGraph_throws_when_multiple_contexts_and_none_specified()
    {
        var options = new CliOptions();
        var result = new ParseResult
        {
            ContextGraphs =
            [
                new ContextGraph { ContextId = "net8.0", EntryRoot = SomeRoot, Graph = new RegistrationGraph() },
                new ContextGraph { ContextId = "net10.0", EntryRoot = SomeRoot, Graph = new RegistrationGraph() }
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CliParserFactory.SelectGraph(result, options));
        Assert.Contains("Multiple application contexts found", ex.Message);
    }

    [Fact]
    public void ResolveExtractionOptions_context_all_sets_all_target_frameworks_when_tfm_unset()
    {
        var options = new CliOptions { ContextAll = true };
        var resolved = CliParserFactory.ResolveExtractionOptions(options);
        Assert.True(resolved.AllTargetFrameworks);
    }

    [Fact]
    public void ResolveExtractionOptions_preserves_explicit_target_framework()
    {
        var options = new CliOptions { TargetFramework = "net10.0" };
        var resolved = CliParserFactory.ResolveExtractionOptions(options);
        Assert.Equal("net10.0", resolved.TargetFramework);
    }
}
