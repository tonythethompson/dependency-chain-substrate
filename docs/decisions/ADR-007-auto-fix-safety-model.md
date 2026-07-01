# ADR-007: Auto-fix / Codemod — Safety Model

**Status:** Accepted
**Date:** 2026-06-28
**Accepted:** 2026-06-29 (Phase 8 kickoff)
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

The tool detects problems (LEAKED, BROKEN, DUPLICATE, ORPHANED). Phase 8 adds
`dcs fix` — applying a safe, reversible source edit to resolve a detected
finding. Write-back carries higher risk than read-only analysis; the safety
model must be explicit before implementation.

---

## Decision

### Q1: v1 fix scope — **DUPLICATE removal only** (amended 8.1b: ORPHANED preview apply)

| Finding | v1 | Notes |
|---------|-----|-------|
| DUPLICATE | **In scope** | Remove lower-confidence duplicate registration line |
| ORPHANED | **In scope (8.1b)** | Remove explicit orphaned registration when eligible; same Roslyn remover + git guards as DUPLICATE |
| LEAKED | Deferred | Preprocessor guards touch more than registration |
| BROKEN | Deferred | Requires code generation |

### Phase 8.1b amendment — ORPHANED `--apply` (Accepted 2026-07-01)

**Eligibility (unchanged from 8.1a preview):** explicit `ParserConfidence`, file:line site,
not composition root, not framework infrastructure (`ILogger*`, `IConfiguration`, etc.).

**Apply guards (same as DUPLICATE):**
- `--preview` default; `--apply` writes files.
- Clean git working tree required unless `--force`.
- Roslyn `RegistrationStatementRemover` at reported line + display name.

**False-positive policy:** Trackdub @ pin `3c4e374d` reports 0 orphaned registrations;
fixture gate (`OrphanedFixTests.Apply_removes_orphan`) is the apply verification path until
a labelled orphan corpus pair exists.

**Rejected for 8.1b:** auto-apply without eligibility filter; runtime-only orphans (no static site).

### Q2: Write-back mechanism — **Roslyn SyntaxRewriter / syntax tree removal**

Remove the parent `ExpressionStatementSyntax` containing the matched
`services.Add*` invocation. `Microsoft.CodeAnalysis.CSharp` is already a
dependency of `DCS.Parser.CSharp`.

Rejected for v1: line-number string deletion (brittle); regex (multiline fragility).

### Q3: Preview model — **`--preview` default, `--apply` to execute**

- Default: print unified diff + fix summary to stdout; no file writes.
- `--apply`: write patched files after guard checks.
- Composable with external `git diff` / CI pipelines.

### Q4: Rollback — **Git-first**

- Before `--apply`, assert clean working tree (`git status --porcelain` empty).
- User rolls back with `git checkout -- .` or `git restore`.
- `--force` bypasses clean-tree check for automation.

Rejected for v1: `.dcs-backup` files; patch-only manual apply.

### Q5: DUPLICATE tie-breaking (which instance to remove)

Ordered removal target (remove the loser):

1. Higher `ParserConfidence` rank loses first (`BlindSpot` > `Degraded` > `Inferred` > `Explicit`).
2. Shorter `SourceLocation.FilePath` (shell file loses to shared composition root).
3. Alphabetically last file path (deterministic).

Preview shows both removed and kept instance locations.

### Q6: Language scope — **C# only for v1**

Spring/Java duplicate removal deferred until Java registration nodes carry stable
`SourceRef` lines for tree-sitter-extracted registrations at parity with Roslyn.

---

## Assumptions

1. Users have git-tracked projects for rollback.
2. DUPLICATE removal false-positive rate is acceptable; Trackdub gate calibrates.
3. Roslyn `RemoveNode` + `NormalizeWhitespace` preserves CI formatting tolerance.

---

## What Would Falsify This Decision

- Roslyn removal causes formatting regressions that fail CI → add explicit trivia handling.
- DUPLICATE removal startup failure rate >5% on real repos → add confirmation gate or narrow patterns.
- Users without git dominate → add file-backup strategy in v1.1.

---

## Implementation

- Module: `DCS.Fix` (`DuplicateFixPlanner`, `OrphanedFixPlanner`, `RegistrationStatementRemover`, `FixEngine`)
- CLI: `dcs fix <repo-path> [--preview|--apply] [--fix-class duplicate|orphaned] [--token <name>] [--all-duplicates] [--force]`
- Gate (DUPLICATE): remove one Trackdub WinUI duplicate; `dcs analyze` shows one fewer duplicate group.
- Gate (ORPHANED): fixture apply removes `IOrphanService`; re-analyze shows one fewer eligible orphan.
