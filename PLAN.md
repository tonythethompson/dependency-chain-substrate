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
| Spring paper-spike (IR compatibility validation) | ⬜ Not started | Phase 0 gate; blocks IR freeze |
| DESIGN.md §4 (Extraction Strategy) — fill answers | ⬜ Not started | After ADR-001 accepted |
| DESIGN.md §5 (IR + Identity) — fill answers | ⬜ Not started | After ADR-002 + spike |
| DESIGN.md §1-3 (Problem, Goals, Users) — fill answers | ⬜ Not started | Parallelisable with above |

**Phase 0 gate:** Spring spike + IR section filled = Planning phase done.

---

## Phase 1 — C# Parser + Analysis + Leakage Detection

**Done means:** Reproduces known Trackdub WinUI leakage on real commits. Blind
spots documented in output.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §6 (C# parser) — fill | ⬜ Not started | Blocked by Phase 0 gate |
| DESIGN.md §7 (Graph analysis) — fill | ⬜ Not started | |
| DESIGN.md §8 (Framework boundary) — fill | ⬜ Not started | |
| Roslyn static parser — implement | ⬜ Not started | |
| IR serialiser — implement | ⬜ Not started | |
| Graph analysis layer — implement | ⬜ Not started | |
| CLI text output — implement | ⬜ Not started | |
| Phase 1 verification against Trackdub | ⬜ Not started | |

---

## Phase 2 — Git Ingestion + Diff Engine

**Done means:** Drift Scanner shows meaningful, low-noise diffs between two
Trackdub commits.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §9-10 — fill | ⬜ Not started | Blocked by Phase 1 gate |
| Git blob reader (libgit2sharp) — implement | ⬜ Not started | |
| Per-commit extraction cache (keyed by SHA) — implement | ⬜ Not started | |
| Diff engine + rename detection — implement | ⬜ Not started | |
| Phase 2 verification against Trackdub | ⬜ Not started | |

---

## Phase 3 — Visualisation + Form Factor

**Done means:** Legible interactive view of Trackdub's graph at full scale.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §11 — fill | ⬜ Not started | Blocked by Phase 2 gate |
| Visualisation consumer — implement | ⬜ Not started | Form factor per ADR-003 |
| Aggregation / focus+context / LOD | ⬜ Not started | |
| Phase 3 verification | ⬜ Not started | |

---

## Phase 4 — Polish + CI Gate

**Done means:** Non-interactive gate runnable in CI.

| Task | Status | Notes |
|------|--------|-------|
| CI-gate consumer — implement | ⬜ Not started | |
| Registration Atlas polish | ⬜ Not started | |
| Boundary Probe config UX | ⬜ Not started | |
| Phase 4 verification | ⬜ Not started | |

---

## Deferred (designed-around, not planned)

- Second language parser (Spring / TS / Python)
- IDE extension
- Auto-fix / codemod
- Runtime enrichment overlay
