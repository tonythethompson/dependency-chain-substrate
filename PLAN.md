# Dependency Chain Substrate — Milestone Tracker

Last updated: 2026-06-28

---

## Phase 0 — Positioning + Extraction Fork + IR Draft

**Done means:** All four gating decisions written as ADRs (Accepted). Spring
paper-spike complete. IR either survives the spike or is revised.

| Task | Status | Notes |
|------|--------|-------|
| Scaffold docs (AGENTS.md, DESIGN.md, PLAN.md, decisions/) | ✅ Done | 2026-06-28 |
| ADR-001: Extraction strategy | ✅ Done | 2026-06-28 — Static-first accepted |
| ADR-002: IR + identity model | ✅ Done | 2026-06-28 — Multi-factor identity |
| ADR-003: Form factor | ✅ Done | 2026-06-28 — CLI-first accepted |
| ADR-004: Spring paper-spike timing | ✅ Done | 2026-06-28 — Spike before IR freeze |
| Spring paper-spike (IR compatibility validation) | ✅ Done | 2026-06-28 — No breaking changes; 5 additive extensions folded into ADR-002 |
| DESIGN.md §4 (Extraction Strategy) — fill answers | ⬜ Not started | After ADR-001 accepted |
| DESIGN.md §5 (IR + Identity) — fill answers | ⬜ Not started | After ADR-002 + spike |
| DESIGN.md §1-3 (Problem, Goals, Users) — fill answers | ⬜ Not started | Parallelisable with above |

**Phase 0 gate:** ✅ CLOSED — Spring spike complete, IR frozen, ADR-002 Accepted.

---

## Phase 1 — C# Parser + Analysis + Leakage Detection

**Done means:** Reproduces known Trackdub WinUI leakage on real commits. Blind
spots documented in output.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §6 (C# parser) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §7 (Graph analysis) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §8 (Framework boundary) — fill | ✅ Done | 2026-06-28 |
| Roslyn static parser — implement | ✅ Done | 2026-06-28 — DCS.Parser.CSharp |
| IR serialiser — implement | ✅ Done | 2026-06-28 — DCS.Core.Serialization |
| Graph analysis layer — implement | ✅ Done | 2026-06-28 — DCS.Analysis |
| CLI text output — implement | ✅ Done | 2026-06-28 — DCS.Cli `analyze` command |
| Phase 1 verification against Trackdub | ⬜ Blocked | Needs Trackdub repo path from user |

**Phase 1 gate:** ⏳ OPEN — implementation complete; Trackdub acceptance test blocked on repo path.

---

## Phase 2 — Git Ingestion + Diff Engine

**Done means:** Drift Scanner shows meaningful, low-noise diffs between two
Trackdub commits.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §9-10 — fill | ⬜ Not started | |
| Git blob reader (libgit2sharp) — implement | ✅ Done | 2026-06-28 — CSharpStaticParser.ParseCommit |
| Per-commit extraction cache (keyed by SHA) — implement | ⬜ Not started | In-memory; file cache deferred |
| Diff engine + rename detection — implement | ✅ Done | 2026-06-28 — DCS.Diff |
| CLI `diff` command — implement | ✅ Done | 2026-06-28 |
| Phase 2 verification against Trackdub | ⬜ Blocked | Needs Trackdub repo path |

**Phase 2 gate:** ⏳ OPEN — diff engine complete; verification blocked on Trackdub path.

---

## Phase 3 — Visualisation + Form Factor

**Done means:** Legible interactive view of Trackdub's graph at full scale.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §11 — fill | ⬜ Not started | |
| Visualisation consumer — implement | ✅ Done | 2026-06-28 — DCS.Viz self-contained HTML |
| CLI `viz` command — implement | ✅ Done | 2026-06-28 |
| Aggregation / focus+context / LOD | ✅ Done | 2026-06-28 — framework-grouped layout + zoom LOD |
| Phase 3 verification | ⬜ Blocked | Needs Trackdub repo path for full-scale test |

**Phase 3 gate:** ⏳ OPEN — implementation complete; full-scale verification blocked on Trackdub path.

---

## Phase 4 — Polish + CI Gate

**Done means:** Non-interactive gate runnable in CI.

| Task | Status | Notes |
|------|--------|-------|
| CI-gate consumer — implement | ✅ Done | 2026-06-28 — exit code 1 on errors; `analyze` is CI-ready |
| Registration Atlas polish | ⬜ Not started | DESIGN.md §12 module spec |
| Boundary Probe config UX | ⬜ Not started | `--frameworks <json>` flag |
| Phase 4 verification | ⬜ Blocked | Needs Trackdub path |

**Phase 4 gate:** ⏳ OPEN — CI gate functional; polish tasks and acceptance test pending.

---

## Deferred (designed-around, not planned)

- Second language parser (Spring / TS / Python)
- IDE extension
- Auto-fix / codemod
- Runtime enrichment overlay
- Per-commit disk cache (currently in-memory; file cache with SHA+parserVersion key deferred)
- `--frameworks <json>` custom framework boundary config
