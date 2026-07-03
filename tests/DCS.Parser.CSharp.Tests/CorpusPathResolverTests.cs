using DCS.Verification;

namespace DCS.Parser.CSharp.Tests;

public sealed class CorpusPathResolverTests
{
    [Fact]
    public void Resolve_with_defaults_fails_in_ci_when_expected_corpus_path_is_missing()
    {
        var oldCi = Environment.GetEnvironmentVariable("CI");
        var oldGithubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        var oldCorpusPath = Environment.GetEnvironmentVariable("DCS_CORPUS_PATH");
        var oldPrimary = Environment.GetEnvironmentVariable("DCS_TEST_MISSING_CORPUS");
        var oldLegacy = Environment.GetEnvironmentVariable("DCS_TEST_MISSING_CORPUS_LEGACY");
        var missing = Path.Combine(Path.GetTempPath(), $"dcs-missing-corpus-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("CI", "true");
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
            Environment.SetEnvironmentVariable("DCS_CORPUS_PATH", null);
            Environment.SetEnvironmentVariable("DCS_TEST_MISSING_CORPUS", null);
            Environment.SetEnvironmentVariable("DCS_TEST_MISSING_CORPUS_LEGACY", null);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                CorpusPathResolver.ResolveWithDefaults(
                    primaryEnvVar: "DCS_TEST_MISSING_CORPUS",
                    legacyEnvVar: "DCS_TEST_MISSING_CORPUS_LEGACY",
                    defaultLocalPath: string.Empty,
                    tempCloneDirName: $"dcs-missing-corpus-{Guid.NewGuid():N}",
                    workspaceRelativeCheckoutPath: "corpus/missing"));

            Assert.Contains("Corpus path is required in CI", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", oldCi);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", oldGithubActions);
            Environment.SetEnvironmentVariable("DCS_CORPUS_PATH", oldCorpusPath);
            Environment.SetEnvironmentVariable("DCS_TEST_MISSING_CORPUS", oldPrimary);
            Environment.SetEnvironmentVariable("DCS_TEST_MISSING_CORPUS_LEGACY", oldLegacy);
            if (Directory.Exists(missing))
                Directory.Delete(missing, recursive: true);
        }
    }

    [Fact]
    public void Resolve_with_defaults_still_returns_null_locally_when_corpus_path_is_missing()
    {
        var oldCi = Environment.GetEnvironmentVariable("CI");
        var oldGithubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        var oldCorpusPath = Environment.GetEnvironmentVariable("DCS_CORPUS_PATH");

        try
        {
            Environment.SetEnvironmentVariable("CI", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
            Environment.SetEnvironmentVariable("DCS_CORPUS_PATH", null);

            var path = CorpusPathResolver.ResolveWithDefaults(
                primaryEnvVar: "DCS_TEST_MISSING_CORPUS",
                legacyEnvVar: "DCS_TEST_MISSING_CORPUS_LEGACY",
                defaultLocalPath: string.Empty,
                tempCloneDirName: $"dcs-missing-corpus-{Guid.NewGuid():N}",
                workspaceRelativeCheckoutPath: "corpus/missing");

            Assert.Null(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", oldCi);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", oldGithubActions);
            Environment.SetEnvironmentVariable("DCS_CORPUS_PATH", oldCorpusPath);
        }
    }
}
