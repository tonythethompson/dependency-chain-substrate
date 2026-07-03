using DCS.Analysis;
using DCS.Fix;

namespace DCS.Fix.Tests;

public sealed class FixApplyRollbackTests
{
    [Fact]
    public void Verify_after_apply_rolls_back_when_post_apply_analysis_throws()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-fix-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Reg.cs");
        const string original = "original";
        const string updated = "updated";
        File.WriteAllText(path, updated);

        try
        {
            var patches = new[] { new FilePatch("Reg.cs", original, updated) };
            var before = new GraphAnalyzer(new()).Analyze();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                FixSafetyGuard.VerifyAfterApplyOrRollback(
                    before,
                    root,
                    patches,
                    () => throw new InvalidOperationException("post-apply parse failed")));

            Assert.Contains("Fix rolled back", ex.Message);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
