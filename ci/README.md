# Corpus gates (CI)

Ground-truth verification runs against pinned external repositories. Configuration lives in
[`corpus-gates.json`](corpus-gates.json) — not in the workflow job names.

## Adding a gate

1. Add an entry to `corpus-gates.json` with:
   - `id` — stable slug used in xUnit traits and CI matrix (e.g. `csharp-migration`)
   - `runner`, `dotnet`, `testProject`
   - `repository`, `ref`, `checkoutPath`
   - `pathEnv` — documented local override env var (CI sets `DCS_CORPUS_PATH` automatically)
   - `privateCheckout` — `true` if the repo needs a PAT

2. Tag gate tests with xUnit traits:
   ```csharp
   [Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
   [Trait(CorpusGateTraits.CorpusIdName, "<id>")]
   ```

3. Resolve the corpus path via `CorpusPathResolver` (checks `DCS_CORPUS_PATH`, then `pathEnv`, then legacy env vars).

## CI jobs

| Job | Purpose |
|-----|---------|
| `build-test` | Unit/fixture tests (`Category!=CorpusGate`) |
| `corpus-matrix` | Loads gate matrix from `corpus-gates.json` |
| `corpus-gate` | One matrix leg per gate entry |

## Secrets

Private corpus repos need `CORPUS_CHECKOUT_PAT` (or legacy `TRACKDUB_PAT`) on the GitHub repository.

## Local runs

```bash
# C# migration corpus
set CORPUS_CSHARP_MIGRATION_PATH=A:\Trackdub
dotnet test tests/DCS.Parser.CSharp.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
dotnet test tests/DCS.Runtime.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
dotnet test tests/DCS.Diff.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
dotnet test tests/DCS.Fix.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"

# C# negative-control corpus (public OSS Avalonia single-shell)
set CORPUS_CSHARP_NEGATIVE_CONTROL_PATH=C:\path\to\StabilityMatrix
dotnet test tests/DCS.Parser.CSharp.Tests --filter "Category=CorpusGate&CorpusId=csharp-negative-control"

# Java Spring corpus
set CORPUS_JAVA_SPRING_PATH=/path/to/spring-petclinic
dotnet test tests/DCS.Parser.Java.Tests --filter "Category=CorpusGate&CorpusId=java-spring"
```

Legacy env vars (`TRACKDUB_PATH`, `PETCLINIC_PATH`) still work.

## Runtime enrichment fixture (Phase 9)

Pinned JSONL follows the Trackdub pin short SHA: `tests/fixtures/corpus/csharp-migration/runtime-<pin8>.jsonl`
(e.g. `runtime-b57fc832.jsonl` for pin `b57fc8327e4773fb686cc77025d2b57bbb37cb85`).

Legacy fixture `runtime-3c4e374d.jsonl` retained for history; gate tests resolve path from `TrackdubPin.CommitSha`.

Regenerate after composition changes (run from Trackdub repo root so model manifest resolves):

```bash
dotnet run --project tools/TrackdubRuntimeProbe -- \
  --trackdub-root A:\Trackdub \
  --out tests/fixtures/corpus/csharp-migration/runtime-b57fc832.jsonl
```

Gate test: `dotnet test tests/DCS.Runtime.Tests --filter Trackdub_runtime_enrichment_gate`

## Global tool releases

Workflow: [`.github/workflows/release.yml`](../.github/workflows/release.yml)

| Trigger | Result |
|---------|--------|
| Push tag `v*` (e.g. `v0.1.1`) | Test → pack → GitHub Release + `.nupkg`; NuGet.org via trusted publisher (`release.yml`, OIDC) |
| Manual **release** workflow | Same pipeline; creates a **draft** GitHub Release |

Release version must match `src/DCS.Cli/DCS.Cli.csproj` `<Version>` (validated in CI).

## Semantic quality (optional, not in CI matrix)

Portable TFM extraction floor separate from migration pin gates:

```bash
dotnet test tests/DCS.Parser.CSharp.Tests --filter CorpusId=csharp-migration-quality
```
