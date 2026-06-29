using DCS.Parser.CSharp.Semantic;
using Microsoft.CodeAnalysis;

namespace DCS.Parser.CSharp;

public sealed class SemanticRegistrationContext
{
    public required ProjectTargetScope Scope { get; init; }
    public SemanticModel? Model { get; init; }
    public int RegistrationOrdinal { get; set; }

    public SemanticTypeResolver? Resolver =>
        Model != null ? new SemanticTypeResolver(Model, Scope.ScopeId) : null;
}
