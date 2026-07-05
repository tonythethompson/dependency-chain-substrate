using DCS.Fix;

namespace DCS.Cli.Tests;

public sealed class FixBuildVerificationTests
{
    [Fact]
    public void Verify_build_after_apply_rolls_back_patches_when_build_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-build-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Reg.cs");
        const string original = "original";
        const string updated = "updated";
        File.WriteAllText(path, updated);

        try
        {
            var patches = new[] { new FilePatch("Reg.cs", original, updated) };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ProgramCommands.VerifyBuildAfterApply(
                    root,
                    patches,
                    _ => new BuildVerificationResult(false, 1, "compile failed")));

            Assert.Contains("Fix rolled back", ex.Message);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Verify_build_after_apply_leaves_patches_when_build_passes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-build-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Reg.cs");
        const string original = "original";
        const string updated = "updated";
        File.WriteAllText(path, updated);

        try
        {
            var patches = new[] { new FilePatch("Reg.cs", original, updated) };

            ProgramCommands.VerifyBuildAfterApply(
                root,
                patches,
                _ => new BuildVerificationResult(true, 0, "ok"));

            Assert.Equal(updated, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
