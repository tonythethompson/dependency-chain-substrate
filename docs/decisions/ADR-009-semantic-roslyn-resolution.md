# ADR-009: Semantic Roslyn Type Resolution

**Status:** Proposed
**Date:** 2026-06-29
**Effort:** High (Opus-class; see AGENTS.md routing matrix)
**Supersedes:** DESIGN.md §5 parked answer ("syntactic only / neither Compilation")

---

## Context

ADR-001 committed to Roslyn in-memory compilation from git blob source with constructor
injection resolved via semantic model. Phase 1–8 shipped syntactic-only extraction.
Short-name `TypeRef.FullyQualifiedName` values cause duplicate-ID collisions, weak
edge wiring, and ambiguous strict-duplicate grouping.

This ADR closes four P0 holes identified during plan review: graph-node vs service-type
identity split, assembly-scoped type identity, per-project-target compilation scope,
and target-specific reference-profile closure.

---

## Decision

### Q1: Compilation — per `ProjectTargetScope`, orphan per root bucket

One ad-hoc `CSharpCompilation` per discovered project target scope, plus orphan fallback
scopes for files not attributable to a project. No MSBuildWorkspace, no restore, no
repo-wide merge, no merged orphan compilation.

See implementation: `ProjectTargetScope`, `ProjectTargetScopeCompilationFactory`.

### Q2: References — `ReferenceProfileProvider.Get(scope)` + topological closure

Reference profiles are target-TFM-specific. Project references compile in topological
order and emit as metadata references when closure is clean. Profile fingerprint is
part of the extraction cache key.

### Q3: Semantic scope

In scope: registration type arguments, constructor deps (resolved edges only),
registration API verification against Microsoft.Extensions.DependencyInjection,
`#if` via known `DefineConstants`, implicit usings (modeled or flagged).

Out of scope: factory lambda bodies, assembly scanning expansion, full MSBuild eval,
runtime enrichment (ADR-008).

### Q4: Three independent quality dimensions

- `TypeResolutionQuality`: Resolved | SyntacticFallback | Error
- `RegistrationRecognitionQuality`: VerifiedMicrosoftDI | SyntaxCandidateUnverified | UnsupportedPattern
- `StrictDuplicateEligibility` (derived): resolved service identity AND verified Microsoft DI AND known composition scope AND NOT project_evaluation_incomplete

### Q5: Identity split (P0)

```text
RegistrationInstanceId = hash(scopeId + path + span + ordinal)  // graph node key; Id aliases this
ServiceTypeIdentity    = ResolvedTypeIdentity | syntactic display
DuplicateGroupKey      = CompositionScopeId + canonical service type identity
```

Strict `FindDuplicates()` groups by `DuplicateGroupKey` where `StrictDuplicateEligibility=true`.

### Q6: Edges

Resolved ctor dep + resolved matching registration → `DependencyEdge` (instance id → instance id).
Otherwise → `UnresolvedInjection`. No asserted edge from syntactic short names.

### Q7: Cross-snapshot diff (GraphDiffer)

Hybrid policy: match by `DuplicateGroupKey` → file path + registration statement fingerprint + ordinal → line proximity last. Ambiguous → remove+add.

### Q8: Multi-TFM default

Default `--all-target-frameworks` (one graph per TFM). Override `--target-framework <tfm>` for single graph.

### Q9: Phase 8 fix

Unchanged removal mechanism; applies to strict DUPLICATE only.

---

## Verification

Primary falsifier: mandatory Trackdub fixture at commit `3c4e374d` (see `tests/verification/TrackdubPin.cs`).

Three metrics reported: `semantic_type_resolution_rate`, `registration_api_verification_rate`, `project_scope_completeness_rate`.

---

## Rejected alternatives

- Single repo-wide compilation
- MSBuildWorkspace / dotnet restore
- NuGet glob reference discovery as correctness path
- Fallback short name in `TypeRef.FullyQualifiedName`
- Opt-in `--semantic` default-off

---

## References

- ADR-001 (extraction strategy)
- ADR-002 amendment (schema 1.2.0 identity model)
- PLAN.md Phase 10
