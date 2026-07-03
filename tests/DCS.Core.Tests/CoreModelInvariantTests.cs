using DCS.Core.IR;

namespace DCS.Core.Tests;

public sealed class CoreModelInvariantTests
{
    [Fact]
    public void ComputeRegistrationInstanceId_is_deterministic_for_same_inputs()
    {
        var a = RegistrationNode.ComputeRegistrationInstanceId("scope1", "File.cs", 1, 2, 3, 4, 0);
        var b = RegistrationNode.ComputeRegistrationInstanceId("scope1", "File.cs", 1, 2, 3, 4, 0);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeRegistrationInstanceId_differs_when_ordinal_differs()
    {
        var a = RegistrationNode.ComputeRegistrationInstanceId("scope1", "File.cs", 1, 2, 3, 4, 0);
        var b = RegistrationNode.ComputeRegistrationInstanceId("scope1", "File.cs", 1, 2, 3, 4, 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeRegistrationInstanceId_differs_when_scope_differs()
    {
        var a = RegistrationNode.ComputeRegistrationInstanceId("scope1", "File.cs", 1, 2, 3, 4, 0);
        var b = RegistrationNode.ComputeRegistrationInstanceId("scope2", "File.cs", 1, 2, 3, 4, 0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeRegistrationInstanceId_tolerates_null_file_path()
    {
        var id = RegistrationNode.ComputeRegistrationInstanceId("scope1", null, 1, 2, 3, 4, 0);
        Assert.NotNull(id);
        Assert.NotEmpty(id);
    }

    [Fact]
    public void ComputeDuplicateGroupKey_is_deterministic_for_same_service_type()
    {
        var identity = ServiceTypeIdentity.FromSyntactic("IFoo");
        var a = RegistrationNode.ComputeDuplicateGroupKey("scope1", identity);
        var b = RegistrationNode.ComputeDuplicateGroupKey("scope1", identity);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeDuplicateGroupKey_differs_across_scopes_for_same_service_type()
    {
        var identity = ServiceTypeIdentity.FromSyntactic("IFoo");
        var a = RegistrationNode.ComputeDuplicateGroupKey("scope1", identity);
        var b = RegistrationNode.ComputeDuplicateGroupKey("scope2", identity);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ServiceTypeIdentity_IsResolved_false_for_syntactic()
    {
        var identity = ServiceTypeIdentity.FromSyntactic("IFoo");
        Assert.False(identity.IsResolved);
        Assert.Equal("syntactic:IFoo", identity.CanonicalKey);
        Assert.Equal("IFoo", identity.DuplicateGroupingKey);
    }

    [Fact]
    public void ServiceTypeIdentity_IsResolved_true_for_resolved()
    {
        var resolved = new ResolvedTypeIdentity
        {
            AssemblyKey = AssemblyKey.FromMetadata("MyAssembly"),
            MetadataName = "MyApp.IFoo"
        };
        var identity = ServiceTypeIdentity.FromResolved(resolved);

        Assert.True(identity.IsResolved);
        Assert.Equal(resolved.CanonicalKey, identity.CanonicalKey);
        Assert.Equal("MyApp.IFoo", identity.DuplicateGroupingKey);
    }

    [Fact]
    public void ServiceTypeIdentity_DuplicateGroupingKey_includes_generic_type_arguments()
    {
        var argIdentity = new ResolvedTypeIdentity
        {
            AssemblyKey = AssemblyKey.FromMetadata("MyAssembly"),
            MetadataName = "System.String"
        };
        var resolved = new ResolvedTypeIdentity
        {
            AssemblyKey = AssemblyKey.FromMetadata("MyAssembly"),
            MetadataName = "MyApp.IRepository`1",
            TypeArguments = [argIdentity]
        };
        var identity = ServiceTypeIdentity.FromResolved(resolved);

        Assert.Equal("MyApp.IRepository`1|System.String", identity.DuplicateGroupingKey);
    }

    [Fact]
    public void AssemblyKey_Canonical_uses_scope_prefix_for_source_scope()
    {
        var key = AssemblyKey.FromProjectScope("MyProject");
        Assert.Equal("scope:MyProject", key.Canonical);
    }

    [Fact]
    public void AssemblyKey_Canonical_uses_simple_name_when_no_public_key_token()
    {
        var key = AssemblyKey.FromMetadata("MyAssembly");
        Assert.Equal("MyAssembly", key.Canonical);
    }

    [Fact]
    public void AssemblyKey_Canonical_includes_version_and_token_when_present()
    {
        var key = AssemblyKey.FromMetadata("MyAssembly", "1.0.0.0", "abcdef1234567890");
        Assert.Equal("MyAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=abcdef1234567890", key.Canonical);
    }

    [Fact]
    public void ResolvedTypeIdentity_ToDisplayName_strips_nested_type_and_generic_arity()
    {
        var identity = new ResolvedTypeIdentity
        {
            AssemblyKey = AssemblyKey.FromMetadata("MyAssembly"),
            MetadataName = "MyApp.Outer+Inner`1"
        };
        Assert.Equal("Inner", identity.ToDisplayName());
    }

    [Fact]
    public void ResolvedTypeIdentity_ComputeHash_is_deterministic()
    {
        var a = ResolvedTypeIdentity.ComputeHash("key1");
        var b = ResolvedTypeIdentity.ComputeHash("key1");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ResolvedTypeIdentity_ComputeHash_differs_for_different_keys()
    {
        var a = ResolvedTypeIdentity.ComputeHash("key1");
        var b = ResolvedTypeIdentity.ComputeHash("key2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TypeRef_FromQualifiedName_splits_namespace_and_short_name()
    {
        var typeRef = TypeRef.FromQualifiedName("MyApp.Services.IFoo");
        Assert.Equal("IFoo", typeRef.ShortName);
        Assert.Equal("MyApp.Services", typeRef.Namespace);
        Assert.Equal("MyApp.Services.IFoo", typeRef.FullyQualifiedName);
    }

    [Fact]
    public void TypeRef_FromQualifiedName_without_dot_has_null_namespace()
    {
        var typeRef = TypeRef.FromQualifiedName("IFoo");
        Assert.Equal("IFoo", typeRef.ShortName);
        Assert.Null(typeRef.Namespace);
    }

    [Fact]
    public void TypeRef_FromShortName_uses_short_name_for_both_fields()
    {
        var typeRef = TypeRef.FromShortName("IFoo");
        Assert.Equal("IFoo", typeRef.ShortName);
        Assert.Equal("IFoo", typeRef.FullyQualifiedName);
        Assert.Null(typeRef.Namespace);
    }
}
