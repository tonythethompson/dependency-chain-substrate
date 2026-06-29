using DCS.Cli;

namespace DCS.Cli.Tests;

public sealed class RepoLanguageDetectorTests
{
    [Fact]
    public void Detects_java_from_pom_xml()
    {
        var root = CreateRepo(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "pom.xml"), "<project/>");
            File.WriteAllText(Path.Combine(dir, "App.java"), "class App {}");
        });

        try
        {
            Assert.Equal(RepoLanguage.Java, RepoLanguageDetector.Resolve(root, RepoLanguage.Auto));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detects_csharp_from_csproj()
    {
        var root = CreateRepo(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"/>");
            File.WriteAllText(Path.Combine(dir, "App.cs"), "class App {}");
        });

        try
        {
            Assert.Equal(RepoLanguage.CSharp, RepoLanguageDetector.Resolve(root, RepoLanguage.Auto));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Explicit_language_flag_overrides_detection()
    {
        var root = CreateRepo(dir => File.WriteAllText(Path.Combine(dir, "pom.xml"), "<project/>"));
        try
        {
            Assert.Equal(RepoLanguage.CSharp, RepoLanguageDetector.Resolve(root, RepoLanguage.CSharp));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRepo(Action<string> write)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-cli-lang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        write(root);
        return root;
    }
}
