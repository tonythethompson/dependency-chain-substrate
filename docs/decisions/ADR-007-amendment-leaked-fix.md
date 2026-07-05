# ADR-007 Amendment: LEAKED Fix (Framework Guard Codemod)

**Status:** Accepted
**Date:** 2026-07-03
**Parent:** [ADR-007](ADR-007-auto-fix-safety-model.md)
**Accepted:** 2026-07-03
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

> LEAKED fix **synthesizes new source** (framework guard logic), not merely removes or
> rewrites a single registration statement.

---

## Context

Phase 8 shipped removal/rewrite fix classes with git rollback and graph guards
(8.1c LEAKED *guard* on apply — prevents other fixes from worsening leakage).
The LEAKED **codemod** itself remains deferred in ADR-007 Q1.

LEAKED findings indicate a registration reachable from a composition root tagged
with framework A while the implementation or dependency closure carries framework B
tags — typical mid-migration duplicate-shell state on Trackdub.

A fix must wrap or guard the registration so it compiles only in the intended shell,
without deleting migration-progress registrations the team still needs.

---

## Decisions

### Q1: Fix action — **Insert framework guard around registration statement**

For each eligible LEAKED finding with a concrete `file:line` site on an explicit
`services.Add*` / `TryAdd*` statement:

1. Identify the **intended shell** from the composition root's `framework_tags`
   (the root where leakage was detected).
2. Insert a preprocessor or platform guard wrapping the registration statement
   so it is active only in that shell.

**Rejected:** delete the registration (that's DUPLICATE/ORPHANED territory, not LEAKED);
move registration to another file without guard (requires cross-file reasoning v2).

### Q2: Guard idiom per framework tag

| Framework tag | Guard idiom (v1) | Notes |
|---------------|------------------|-------|
| `winui` | `#if WINUI` … `#endif` | Match Trackdub existing shell guards |
| `avalonia` | `#if AVALONIA` … `#endif` | Match Trackdub Avalonia shell |
| `wpf` | `#if WPF` … `#endif` | For WPF-only corpora |
| `maui` | `#if MAUI` … `#endif` | Deferred until MAUI corpus exists |
| Custom (`--frameworks` json) | **Not auto-fixed in v1** | Emit preview-only message; user adds manual guard |

Guard symbols MUST match symbols already defined in the target project's `.csproj`
(`DefineConstants`) or documented project convention. If no matching constant exists,
fix is **ineligible** (preview lists reason: `no_guard_constant`).

**Open question for sign-off:** Should v1 support `OperatingSystem.Is*` runtime guards
instead of preprocessor guards for registrations inside methods without top-level
statement support? **Proposed: no** — preprocessor only in v1; runtime guards deferred.

### Q3: Eligibility filter

A LEAKED node is eligible for `--fix-class leaked` apply only when **all** hold:

1. `ParserConfidence` is `explicit` or `inferred` (not `degraded` / `blind_spot`).
2. Finding has at least one `site` with `file_path` + `line` on the registration statement.
3. Statement is a single `ExpressionStatementSyntax` (not nested inside lambda body).
4. Composition root `framework_tags` contain exactly one actionable shell tag from the
   table in Q2 (not multi-tagged roots).
5. Guard constant for that tag is defined in the containing project's compile constants.
6. Registration is not the composition root entry (`AddTrackdub`-style extension methods excluded).

**Rejected for v1:** fix LEAKED blind spots; fix without file:line; multi-hop guard insertion.

### Q4: Safety model — **stricter than removal fixes**

| Guard | DUPLICATE / ORPHANED / BROKEN | LEAKED (proposed) |
|-------|------------------------------|-------------------|
| `--preview` default | Yes | Yes |
| Clean git tree | Yes | Yes |
| Post-apply graph re-analyze | Yes | Yes |
| LEAKED guard (no worsening) | Yes | Yes |
| BROKEN guard | BROKEN class only | Yes |
| `--verify-build` | Optional (8.1e) | **Mandatory** — not overridable in v1 |
| Negative-control corpus gate | N/A | Must stay 0 LEAKED / 0 DUPLICATE after apply on `csharp-negative-control` |

**Rationale:** Adding guards changes compilation units and can introduce subtle errors
(wrong `#if` branch, duplicate symbols across shells). Build verification is necessary
but not sufficient; graph guards remain.

**Rejected:** optional `--verify-build` for LEAKED; apply without re-analyze.

### Q5: Write-back mechanism

Roslyn `SyntaxRewriter` wrapping the parent `ExpressionStatementSyntax` in guarded
`IfDirective` / `EndIfDirective` trivia pair (or equivalent `PreprocessorDirective` nodes).

Not line insertion — must preserve formatting via `NormalizeWhitespace` and run
`dotnet format` only if build verification fails formatting-sensitive projects (defer).

### Q6: Verification gates

| Gate | Corpus | Criterion |
|------|--------|-----------|
| Preview | `tests/fixtures/di-patterns/` | Synthetic LEAKED fixture shows unified diff with `#if` guard only |
| Apply + rollback | Fixture | Guard removes LEAKED finding; re-analyze count decreases |
| False-positive | `csharp-negative-control` (StabilityMatrix @ pin) | `dcs fix --apply` must not be invocable (0 eligible) OR no file writes |
| True-positive | Trackdub @ pin `3c4e374d` | Optional gate: apply one labelled LEAKED site; LEAKED count decreases; build passes with `--verify-build` |

Trackdub optional gate may be behind `CORPUS_CSHARP_MIGRATION_PATH` skip pattern
(same as other Trackdub-only gates). Fixture + negative-control gates are mandatory in CI.

### Q7: CLI surface

```text
dcs fix <repo> --fix-class leaked [--preview|--apply] [--token <abstract>] [--force]
```

`--verify-build` is implied when `--apply` and `--fix-class leaked`; explicit flag
accepted as alias. `--apply` without build verification rejected for this class.

---

## Assumptions

1. Trackdub `@ pin` retains real LEAKED findings suitable for optional apply gate.
2. Preprocessor guard constants (`WINUI`, `AVALONIA`) are already defined in migration repos.
3. Users accept that LEAKED fix is C#-only (same as ADR-007 Q6).

---

## What Would Falsify This Amendment

- Roslyn guard insertion breaks more than 5% of eligible sites on formatting → trivia rework.
- `--verify-build` passes but semantic leakage remains → tighten eligibility or add second analyze pass.
- Negative-control corpus shows DUPLICATE after guard insertion on unrelated repo → halt LEAKED apply.
- Majority of LEAKED sites lack preprocessor constants → pivot to runtime-guard v2 or remain preview-only.

---

## Status: Accepted

Signed off 2026-07-03. Fixture gate: `tests/fixtures/di-patterns-leaked-guard/` +
`LeakedGuardFixtureTests`. **Preview shipped** (`LeakedFixPlanner`, `dcs fix --fix-class leaked --preview`).
**Apply shipped** with mandatory build verification and post-apply graph guards.
