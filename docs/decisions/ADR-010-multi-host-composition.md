# ADR-010: Multi-Host Composition Islands

Status: Accepted  
Date: 2026-07-05  
Deciders: Principal architect (DCS Phase Next)

## Context

Trackdub @ `5fd8b481` is no longer a single DI graph. It has multiple composition roots:

- **Desktop:** `CompositionRoot.cs`, `App.axaml.cs`, Avalonia playback overrides
- **API/Billing:** `Trackdub.Api/Program.cs`, `BillingServiceCollectionExtensions.cs`
- **Lambda:** `Trackdub.WebhookDelivery/Function.cs` (`ConfigureServices`)

DCS merges all `net10.0` projects into one `ContextGraph` and uses union multi-seed reachability. Registrations valid within the API or Lambda host but unreachable from Desktop seeds were reported as **ORPHANED** (12 @ pin). This is false debt.

## Decision

1. **Introduce `CompositionIsland` attribution** on registration sites via source file path heuristics (`desktop`, `api`, `lambda`, `external`).
2. **Per-island reachability:** BFS from island-specific seed files (not only global union).
3. **Re-tier orphans:**
   - `tier: actionable` — true orphan (zero in-degree, unreachable from own island seeds)
   - `tier: island_valid` — zero in-degree globally but reachable within attributed island
4. **CLI:** `--island <desktop|api|lambda|all>` filters findings; `--no-island-aware` disables re-tiering.
5. **Report section:** `COMPOSITION ISLANDS` summary with seed/reachable/valid/orphan counts per island.

Island-aware analysis is **on by default** for `dcs analyze`.

## Consequences

- Trackdub portable @ pin: **12 global orphans → ~0 true orphans**, ~12 island-valid (Lambda + API subgraphs).
- `unresolved_count` unchanged until P1 parser work (expected).
- `GraphAnalyzer` gains `islandAware` parameter; existing callers default to `false` except CLI analyze.
- Future: cross-island `disconnected_subgraph` warnings when shared abstractions lack edges.

## Alternatives considered

- **Separate ContextGraph per host:** Rejected — duplicates shared library registrations across TFMs/projects; TFM split remains primary axis.
- **Runtime-only island detection:** Deferred — static seeds sufficient for Trackdub gate; runtime fixture validates Lambda/API nodes resolve.
