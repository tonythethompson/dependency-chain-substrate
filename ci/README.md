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

# Java Spring corpus
set CORPUS_JAVA_SPRING_PATH=/path/to/spring-petclinic
dotnet test tests/DCS.Parser.Java.Tests --filter "Category=CorpusGate&CorpusId=java-spring"
```

Legacy env vars (`TRACKDUB_PATH`, `PETCLINIC_PATH`) still work.
