# Dependency Chain Substrate — Agent Guidance

This file governs how AI agents should work in this repository.
It is authoritative. CLAUDE.md, if it exists, defers to this file.

---

## Discipline: Designed / Implemented / Tested / Verified

Every feature goes through four explicitly gated states. An agent MUST NOT
advance a feature past a state it has not actually reached.

| State | Meaning | Evidence required |
|-------|---------|-------------------|
| **Designed** | Architecture decision documented; ADR written or design section complete | ADR file exists and is closed (Status: Accepted) |
| **Implemented** | Code written and compiles | CI passes; no compile errors |
| **Tested** | Unit/integration tests written and passing | Test run output confirms green |
| **Verified** | Behaviour confirmed against ground-truth corpus (Trackdub) | Reproduces known-leakage pattern OR explicitly documents what it cannot reproduce and why |

An agent claiming a feature is "done" without Verified status is making a false
claim. The origin story of this tool is exactly that failure mode.

### Gate enforcement

- Do not commit code that does not compile.
- Do not close a design section without an ADR or a DESIGN.md section with
  explicit key-question answers.
- Do not mark a phase milestone complete without running against Trackdub (once
  Trackdub is wired in Phase 1).
- If you hit a blind spot (cannot resolve a dynamic registration, cannot build
  a historical commit), surface it explicitly in output. Do not silently skip.

---

## Model Routing Matrix

Route design tasks by row. Sonnet executes routine synthesis and doc drafting;
Opus owns irreversible architectural decisions. "Consult" means generate a
candidate with Sonnet but review and own the output with Opus.

| Design task | Model | Effort | Why |
|-------------|-------|--------|-----|
| Extraction strategy fork | Opus | High | Irreversible keystone with derived constraints |
| IR + identity model + cross-language validation | Opus | High | Central bet; generalisation on sample size one |
| Diff identity / rename-move detection | Opus | High | Known-hard; decides whether diffs are usable |
| Viz / form-factor strategic fork | Opus | Med-High | Ecosystem lock-in; somewhat irreversible |
| Plugin contract + IR versioning | Opus | Med-High | Contract expensive to change once parsers exist |
| Framework boundary taxonomy | Opus consult, Sonnet draft | Med | Subtle at edges, bounded scope |
| Graph analysis definitions | Sonnet | High | Precise-definition work; Opus if reachability gets subtle |
| C# parser pattern catalog | Sonnet | Med-High | Enumeration + judgment; Opus for factory-lambda resolution |
| Git ingestion design | Sonnet | Med | Known patterns (libgit2sharp blob read) |
| Module specs | Sonnet | Med | Synthesis once layers fixed |
| Test corpus / dogfood plan | Sonnet | Med | Concrete and bounded |
| Doc drafting / synthesis | Sonnet | Low-Med | Iteration and formatting |
| Risks / open-questions consolidation | Sonnet | Low | Aggregation |

---

## Scope boundaries for subagents

- A subagent must not write implementation code unless all upstream design
  sections it depends on are in **Designed** state.
- A subagent must not alter ADRs. ADRs are closed by the human or the main
  thread after explicit sign-off.
- A subagent may propose ADR amendments as draft text, but must not write to
  `docs/decisions/` directly.
- Git operations (commit, push, PR) require explicit human instruction.

---

## Ground truth corpus

Trackdub is the ground-truth corpus. Every claim about extraction coverage,
leakage detection, or diff fidelity must be checkable against Trackdub.
The Trackdub repo path will be set in `.claude/settings.local.json` under
`TRACKDUB_PATH` when Phase 1 begins. Until then, reference it by name only.

---

## Key file map

| File | Purpose |
|------|---------|
| `planning/00-plan-of-plan.md` | Origin doc; defines structure, phasing, routing matrix |
| `PLAN.md` | Live milestone tracker |
| `docs/DESIGN.md` | The actual design document (skeleton → filled) |
| `docs/decisions/ADR-NNN-*.md` | Architecture decision records |
