# ADR-007: Auto-fix / Codemod — Safety Model

**Status:** Proposed
**Date:** 2026-06-28
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

The tool currently detects problems (LEAKED, BROKEN, DUPLICATE, ORPHANED). The
natural next step is `dcs fix` — applying a safe, reversible source edit to
resolve a detected finding. This is a write-back operation with different risk
than read-only analysis. The safety model must be decided before implementation.

---

## Questions to Resolve

### Q1: Which fix classes are safe enough for v1?

**Safe (unambiguous, low blast radius):**

| Finding | Fix | Risk |
|---------|-----|------|
| DUPLICATE | Remove the lower-confidence duplicate registration line | Low — at worst the app fails to start; easily caught |
| ORPHANED | Remove the unreferenced registration line | Medium — runtime consumers (reflection, hosted services) may still use it |
| LEAKED (framework boundary) | Add `#if WINUI` / `#if AVALONIA` preprocessor guard | Medium — preprocessor change touches more than the registration |

**Not safe for v1 (deferred):**

| Finding | Reason |
|---------|--------|
| BROKEN chain | Fix requires adding a missing registration — new code generation, higher correctness bar |
| Renamed type | Rename touches call sites, not just registration; Roslyn semantic model required |
| Lifetime change | Correct lifetime depends on runtime semantics; static analysis cannot always determine the right value |

**Likely decision:** v1 scope = DUPLICATE removal only. Small, reversible,
unambiguous. ORPHANED removal in v1.1 after false-positive rate is measured.

---

### Q2: Write-back mechanism

**Option A: Roslyn SyntaxRewriter**
- Correct AST-level edit; handles formatting, trivia, comments
- Requires `Microsoft.CodeAnalysis.CSharp` (already a dependency)
- Works on syntactic tree; no semantic model needed for simple line removal
- Best for removing a single `services.Add*()` call

**Option B: Line number–based string replacement**
- Use `SourceRef.Line` from the IR; remove the line from the raw file
- Brittle if the file was modified since extraction (line numbers shifted)
- No trivia handling; may leave blank lines or broken chains

**Option C: Regex replacement**
- Pattern-match the full `services.AddSingleton<IFoo, FooImpl>()` call text
- More robust than line numbers but breaks on multiline registrations

**Likely decision:** Option A (Roslyn SyntaxRewriter) for DUPLICATE removal.
The C# parser already has the Roslyn dependency; SyntaxRewriter is well-tested
for this pattern.

---

### Q3: Preview model

Before applying any fix, the user must see what will change.

**Option A: Print unified diff to stdout**
- `dcs fix --preview` prints `--- a/file.cs +++ b/file.cs @@ -N,M +N,M @@`
- No side effects; user applies with `dcs fix --apply` if satisfied

**Option B: Write patched file to a temp location**
- User can `diff -u original.cs /tmp/dcs-fix/original.cs`
- Slightly higher friction than inline diff

**Option C: Interactive confirmation per fix**
- Prompt "Apply this fix? [y/N]" after showing the diff
- Non-interactive CI mode skips the prompt (requires `--yes` flag)

**Likely decision:** Option A (`--preview` default, `--apply` to execute).
Keeps the tool composable with existing diff tools; CI pipeline uses `--apply`.

---

### Q4: Rollback strategy

**Git-based:** The tool only writes files tracked by git. Before applying,
assert the working tree is clean (no uncommitted changes). User can `git diff`
to see what changed and `git checkout -- .` to rollback. The tool refuses to
run `--apply` on a dirty working tree unless `--force` is passed.

**File-backup:** Before overwriting, write `file.cs.dcs-backup`. Restores with
`dcs fix --undo`. Higher friction but works outside git.

**Dry-run only:** Never write files; always require user to apply the patch
manually via `patch -p1 < fix.patch`. Maximum safety; minimum convenience.

**Likely decision:** Git-based rollback. Assert clean working tree before apply.
This aligns with the target user (developers with git discipline). Add
`--force` escape hatch for CI pipelines that accept dirty trees.

---

### Q5: Multi-file fix scope

DUPLICATE registrations may appear in different files (one in `App.xaml.cs`,
one in `CompositionRoot.cs`). The fix removes one. Which one?

**Tie-breaking rules (ordered):**
1. Lower `ParserConfidence` (DEGRADED loses to EXPLICIT)
2. Shorter registration file path (framework shell file loses to shared composition root)
3. Alphabetically last (deterministic)

The removed instance and the kept instance are shown in the preview diff.

---

## Assumptions

1. Users have git-tracked projects. The rollback model is git-first.
2. DUPLICATE removal is safe enough to be the v1 scope; user feedback will
   calibrate whether ORPHANED removal is safe.
3. Roslyn SyntaxRewriter handles trivia (whitespace, comments) correctly for
   removing a single statement from a method body.

---

## What Would Falsify This Decision

- Roslyn SyntaxRewriter introduces formatting regressions (blank lines, indent
  changes) that trigger CI failures → switch to string replacement with explicit
  blank-line cleanup.
- DUPLICATE removal causes startup failures in >5% of real-world runs (false
  positives) → narrow scope further or add a confirmation gate.
- Users are primarily in environments without git → add file-backup strategy.

---

## Status: Proposed

This ADR will be marked Accepted when Phase 8 begins.
