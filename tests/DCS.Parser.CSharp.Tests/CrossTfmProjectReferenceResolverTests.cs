using DCS.Parser.CSharp.Semantic;

namespace DCS.Parser.CSharp.Tests;

public sealed class CrossTfmProjectReferenceResolverTests
{
    [Theory]
    [InlineData("net10.0", "net10.0-windows10.0.19041.0", true)]
    [InlineData("net10.0-windows10.0.19041.0", "net10.0", false)]
    [InlineData("net10.0", "net10.0", true)]
    public void Reference_compatibility_rules(string provider, string consumer, bool expected) =>
        Assert.Equal(expected, CrossTfmProjectReferenceResolver.IsReferenceCompatible(provider, consumer));

    [Fact]
    public void ExpandWithReferencedScopes_adds_portable_project_reference_for_windows_scope()
    {
        var portable = CreateScope("src/App/App.csproj", "net10.0", ["src/App/Reg.cs"]);
        var windows = CreateScope(
            "src/Shell/Shell.csproj",
            "net10.0-windows10.0.19041.0",
            ["src/Shell/Reg.cs"],
            ["src/App/App.csproj"]);

        var expanded = CrossTfmProjectReferenceResolver.ExpandWithReferencedScopes(
            [windows], [portable, windows]);

        Assert.Contains(expanded, s => s.ScopeId == portable.ScopeId);
        Assert.Contains(expanded, s => s.ScopeId == windows.ScopeId);
    }

    private static ProjectTargetScope CreateScope(
        string csproj,
        string tfm,
        string[] files,
        string[]? projectRefs = null) =>
        new()
        {
            ScopeId = $"{csproj}|{tfm}|Debug",
            CsprojPath = csproj,
            TargetFramework = tfm,
            BuildConfiguration = "Debug",
            AssemblyName = Path.GetFileNameWithoutExtension(csproj),
            SourceMembershipProfileHash = "test",
            SourceFiles = [.. files],
            ProjectReferences = [.. (projectRefs ?? [])],
            DefineConstants = [],
            NullableEnabled = true,
            ImplicitUsingsEnabled = true
        };
}
