# ADR-002 Amendment: Semantic Identity Model (schema 1.2.0)

**Status:** Accepted
**Date:** 2026-06-29
**Accepted:** 2026-06-30
**Parent:** ADR-002 IR Identity Model
**Companion:** ADR-009 Semantic Roslyn Type Resolution

---

## Context

The dual-identity addendum (schema 1.1.0) introduced `InstanceId` to fix LEAKED
suppression for parallel-shell duplicates. Semantic Roslyn (ADR-009) requires a
permanent split between graph node identity and service type identity, plus
assembly-scoped `ResolvedTypeIdentity`.

---

## Decision

### Breaking change: `Id` semantics

**Before:** `Id = hash(FQN)` — service type token, shared across registration sites.

**After:** `Id` is an **alias of `RegistrationInstanceId`** — unique per registration site within a snapshot.

```text
RegistrationInstanceId =
  hash(ProjectTargetScopeId + repo_relative_path + syntax_span + registration_ordinal)
```

`InstanceId` converges to the same formula (may duplicate field one release).

### New fields on `RegistrationNode`

| Field | Purpose |
|-------|---------|
| `RegistrationInstanceId` | Primary graph key (same value as `Id`) |
| `ServiceTypeIdentity` | `ResolvedTypeIdentity` or syntactic fallback |
| `DuplicateGroupKey` | `CompositionScopeId + canonical type identity` |
| `CompositionScopeId` | App/shell/module boundary for duplicate grouping |
| `TypeResolutionQuality` | Type argument resolution quality |
| `RegistrationRecognitionQuality` | DI API verification quality |
| `RegistrationStatementFingerprint` | Normalized invocation hash for diff disambiguation |

### `ResolvedTypeIdentity`

```text
AssemblyKey     // metadata or project-target scope id for source symbols
MetadataName    // CLR metadata form
TypeArguments[] // recursive for constructed generics
```

### Strict duplicate analysis

`FindDuplicates()` groups by `DuplicateGroupKey` where `StrictDuplicateEligibility=true`.
`FindPossibleDuplicates()` covers weaker homonym / fallback / unverified groups.

### GraphDiffer cross-snapshot matching

1. Match by `DuplicateGroupKey`
2. Disambiguate: same repo-relative path + registration statement fingerprint + ordinal
3. Line proximity last tiebreaker
4. Still ambiguous → remove + add (no forced modified match)

### Dependency edges

`DependencyEdge.From` / `To` reference `RegistrationInstanceId`.

---

## Schema version

Remains **1.2.0** (already declared on `RegistrationGraph`). Semantic identity fields are additive with breaking `Id` consumer semantics.

**ParserVersion:** `0.2.0` for semantic C# parser.

---

## Migration notes

- `GraphAnalyzer`, `GraphDiffer`, CLI output: key nodes by `RegistrationInstanceId` / `Id` (now instance-scoped)
- LEAKED instance pass: group by `DuplicateGroupKey` instead of legacy FQN `Id`
- Phase 8 fix: unchanged source-line removal; strict duplicate eligibility gate added
- First diff after upgrade may show more add/remove for moved registrations
