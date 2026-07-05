# Dependency Chain Substrate (DCS)

**Static dependency-injection graph extraction, migration verification, and drift analysis — without requiring your project to build.**

[![CI](https://github.com/tonythethompson/dependency-chain-substrate/actions/workflows/ci.yml/badge.svg)](https://github.com/tonythethompson/dependency-chain-substrate/actions/workflows/ci.yml)

---

## About

Dependency Chain Substrate is a **migration verifier** for dependency injection (DI). It reads source from arbitrary git commits, builds a registration graph, and reports whether a framework migration (for example WinUI → Avalonia) still has **leaked**, **duplicated**, or **broken** wiring.

The tool was born from a real failure mode: during a WinUI-to-Avalonia migration, the full DI graph was invisible, parallel shells registered the same services, and both humans and agents declared the migration “complete” when it was not. DCS exists to **contradict false “we’re good” claims** with objective, file-and-line evidence.

### What it does

| Capability | Command | Purpose |
|------------|---------|---------|
| **Framework Boundary Probe** | `dcs analyze` | Detect `LEAKED`, `DUPLICATE`, `BROKEN`, `ORPHANED`, and `CYCLE` findings across declared framework tags |
| **Drift Scanner / Migration Diff** | `dcs diff` | Compare registration graphs between two commits with rename-aware matching |
| **Registration Atlas** | `dcs atlas`, `dcs dump-ir` | Human-readable listing and versioned JSON IR export |
| **Topology Lens** | `dcs viz` | Self-contained interactive HTML graph at Trackdub scale (300+ nodes) |
| **Path Excavator** | `dcs path` | Shortest dependency path from composition root to a target registration |
| **Topology Lens (path)** | `dcs viz --path-to` | Same path highlighted on interactive HTML graph |
| **Auto-fix (preview/apply)** | `dcs fix` | Safe C# fixes for DUPLICATE, ORPHANED, and simple BROKEN registration findings |

### What makes it different

- **Static-first, git-native** — reads blobs at any commit via LibGit2Sharp; no checkout, no MSBuild restore required for analysis.
- **Migration-focused semantics** — cross-framework leakage and duplicate abstract tokens are first-class findings, not generic code smells.
- **Honest blind spots** — factory lambdas, conditional registration, and unresolved injections are surfaced as `BLIND_SPOT` / `DEGRADED`, not silently dropped.
- **CI-ready** — non-zero exit codes on errors; JSON report schema for automation.
- **Multi-language IR** — C# (Roslyn) and Java/Spring (tree-sitter) parsers share a versioned graph model.

DCS is **not** a runtime profiler, SonarQube replacement, or general architecture linter. It models DI registration call sites and the edges between them.

---

## Quick start

### Prerequisites

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download) (10.0 recommended for full C# semantic gates)
- Git (for repo analysis)

### Build

```bash
git clone https://github.com/tonythethompson/dependency-chain-substrate.git
cd dependency-chain-substrate
dotnet build
```

### Install as a global tool

**From NuGet** (when published):

```bash
dotnet tool install --global DependencyChainSubstrate.Cli
dcs --help
```

**From a local build** (development or pre-release):

```bash
dotnet pack src/DCS.Cli/DCS.Cli.csproj -c Release -o artifacts/nupkg
dotnet tool install --global DependencyChainSubstrate.Cli --add-source ./artifacts/nupkg
dcs --help
```

Upgrade: `dotnet tool update --global DependencyChainSubstrate.Cli`  
Uninstall: `dotnet tool uninstall --global DependencyChainSubstrate.Cli`

The global command is **`dcs`**. Package id: `DependencyChainSubstrate.Cli` (see [CHANGELOG.md](CHANGELOG.md) for version notes).

### Run the CLI

**With the global tool** (after install):

```bash
dcs analyze /path/to/your/repo
```

**Without installing** (development):

```bash
dotnet run --project src/DCS.Cli -- analyze /path/to/your/repo
```

Or build once and invoke `src/DCS.Cli/bin/Debug/net8.0/dcs` directly.

### Analyze a specific commit

```bash
dcs analyze /path/to/repo --commit abc1234
```

Or with `dotnet run --project src/DCS.Cli -- analyze ...` if not using the global tool.

Exit code **0** = no blocking errors. Exit code **1** = `LEAKED` or `BROKEN` findings (CI gate). Exit code **2** = usage error.

---

## Example workflow

Mid-migration desktop app with WinUI and Avalonia shells running in parallel:

```bash
# 1. Full analysis at a pinned mid-migration commit
dotnet run --project src/DCS.Cli -- analyze ./my-app --commit 3c4e374d --verbosity actionable

# 2. Multi-target-framework summary (portable + windows TFMs)
dotnet run --project src/DCS.Cli -- analyze ./my-app --commit 3c4e374d --context all --metrics

# 3. Trace how a service reaches the shell
dotnet run --project src/DCS.Cli -- analyze ./my-app --commit 3c4e374d --context "csharp|net10.0"
dotnet run --project src/DCS.Cli -- path ./my-app --commit 3c4e374d --to VoiceCloneConsentCoordinator --context "csharp|net10.0-windows10.0.19041.0"

# 4. Diff before/after retiring the old shell
dotnet run --project src/DCS.Cli -- diff ./my-app --from 3c4e374d --to 316614b8

# 5. Interactive graph for review
dotnet run --project src/DCS.Cli -- viz ./my-app --commit 3c4e374d --out graph.html

# 6. JSON report for CI
dotnet run --project src/DCS.Cli -- analyze ./my-app --commit 3c4e374d \
  --format json --report-out report.json --text-out report.txt
```

Sample text output sections (in severity order):

```
LEAKED
BROKEN CHAINS
DUPLICATE REGISTRATIONS
ORPHANED
CYCLES
BLIND SPOTS
SUMMARY
```

---

## CLI reference

Run `dotnet run --project src/DCS.Cli -- --help` for full usage.

| Command | Description |
|---------|-------------|
| `analyze <repo>` | Extract DI graph and run leakage analysis |
| `atlas <repo>` | Human-readable registration listing |
| `dump-ir <repo>` | Export IR JSON without analysis |
| `diff <repo> --from <sha> --to <sha>` | Diff two commits |
| `path <repo> --to <registration>` | Dependency path to a registration |
| `enrich <ir-file> --runtime-log <path>` | Merge static IR with runtime JSONL resolution log |
| `viz <repo>` | Generate self-contained HTML visualization |
| `fix <repo>` | Preview/apply C# fixes: duplicate, orphaned, broken, leaked |

### Common options

| Flag | Applies to | Description |
|------|------------|-------------|
| `--commit <sha>` | analyze, atlas, dump-ir, path, viz, fix (preview) | Pin source to a git commit (blob read) |
| `--language auto\|csharp\|java` | analyze, diff, atlas, viz | Parser selection |
| `--context <id>` | analyze, path, fix | Select TFM/context (`all` for summary) |
| `--target-framework <tfm>` | analyze | Single TFM graph |
| `--all-target-frameworks` | analyze | One graph per discovered TFM |
| `--frameworks <json>` | analyze, diff | Additive custom framework boundary config |
| `--format text\|json` | analyze, path | Output format |
| `--report-out <path>` | analyze | Write structured report |
| `--verbosity summary\|actionable\|full` | analyze | Finding detail level |
| `--strict` | analyze | Disable noise suppressions (audit mode) |
| `--metrics` | analyze | Print extraction quality metrics on stderr |
| `--cache-dir <path>` | repo commands | Override extraction cache |
| `--no-cache` | repo commands | Bypass cache (use after parser updates) |
| `--preview` / `--apply` | fix | Preview diff vs write changes (`--apply` requires clean working tree) |
| `--fix-class <kind>` | fix | `duplicate` (default), `orphaned`, `broken`, `leaked` |
| `--token <name>` | fix | Limit fix to one registration token |
| `--all-duplicates` | fix | Preview/apply all strict duplicate fixes |
| `--verify-build` | fix | After `--apply`, run `dotnet build`; rollback patches on failure |

**PowerShell note:** quote pipe characters in context IDs: `--context "csharp|net10.0"`.

**Path disambiguation:** `--to VoiceCloneConsentCoordinator` may match multiple registrations across shells. Use registration id or fully qualified name, or narrow with `--context`.

---

## Architecture

```
┌─────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ Git blobs   │────▶│ Parser layer     │────▶│ IR (DCS.Core)   │
│ (LibGit2)   │     │ C# / Java        │     │ schema 1.2.0    │
└─────────────┘     └──────────────────┘     └────────┬────────┘
                                                      │
                      ┌───────────────────────────────┼───────────────────────────────┐
                      ▼                               ▼                               ▼
              ┌───────────────┐              ┌───────────────┐              ┌───────────────┐
              │ DCS.Analysis  │              │ DCS.Diff      │              │ DCS.Viz       │
              │ leakage, path │              │ commit diff   │              │ HTML graph    │
              └───────┬───────┘              └───────────────┘              └───────────────┘
                      │
                      ▼
              ┌───────────────┐
              │ DCS.Cli       │
              │ dcs analyze…  │
              └───────────────┘
```

### Repository layout

| Path | Role |
|------|------|
| `src/DCS.Core` | IR types, serialization, extraction cache |
| `src/DCS.Parser.CSharp` | Roslyn static + semantic extraction |
| `src/DCS.Parser.Java` | Spring Boot / tree-sitter extraction |
| `src/DCS.Analysis` | Graph analysis, findings, path finder |
| `src/DCS.Diff` | Cross-commit graph differ |
| `src/DCS.Viz` | Self-contained HTML visualization |
| `src/DCS.Fix` | DUPLICATE registration codemod |
| `src/DCS.Cli` | `dcs` command-line entry point |
| `tests/` | Unit, fixture, and corpus gate tests |
| `docs/DESIGN.md` | Design document (problem, IR, modules) |
| `docs/decisions/` | Architecture decision records (ADRs) |
| `docs/schemas/` | JSON schemas (e.g. `analysis-report-1.0.json`) |
| `vscode-extension/` | Phase 7 VS Code extension scaffold (report JSON consumer) |
| `PLAN.md` | Milestone tracker and phase gates |

---

## Languages and scope

### In scope (today)

- **C#** — `Microsoft.Extensions.DependencyInjection` call-site patterns; semantic type resolution per TFM; built-in framework tags (WinUI, Avalonia, WPF, ASP.NET Core, etc.)
- **Java / Spring Boot** — `@Bean`, stereotypes, `@Autowired` wiring; PetClinic integration gate

### Explicitly out of scope (v1)

- General-purpose runtime container profiling (Phase 9 ships a targeted MS.DI runtime enrichment overlay)
- IDE extension (deferred Phase 7)
- TypeScript / Python parsers (parked)
- LEAKED auto-fix and broad codemods beyond the current C# DUPLICATE / ORPHANED / simple BROKEN fix classes
- Full semantic MSBuildWorkspace dependency on project build

---

## Ground truth and verification

DCS is validated against real migration corpora, not toy fixtures alone.

| Corpus | Role | Gate |
|--------|------|------|
| **Trackdub** | Private WinUI→Avalonia mid-migration reference | Parser, runtime, diff-rename, and fix corpus gates |
| **StabilityMatrix** | Public Avalonia single-shell negative control | `CsharpNegativeControlGateTests` (`csharp-negative-control`) |
| **Spring PetClinic** | Java/Spring IR smoke test | `SpringPetClinicIntegrationTests` |
| **di-patterns fixtures** | Parser pattern catalog regression | Golden CLI/JSON tests |

Trackdub pin: `b57fc832` (post–WinUI-retire migration head on GitHub; see `tests/verification/TrackdubPin.cs`)  
StabilityMatrix pin: `d97f6ccb9634a7ccfa7513be083aa70653112147` (analyzes `StabilityMatrix/` subproject)

### Running corpus gates locally

Corpus pins and CI matrix entries are defined in [`ci/corpus-gates.json`](ci/corpus-gates.json). Gate tests are tagged with xUnit traits — not filtered by class name.

**C# migration corpus (`csharp-migration`):**

```bash
set CORPUS_CSHARP_MIGRATION_PATH=A:\Trackdub   # Windows
# or: export CORPUS_CSHARP_MIGRATION_PATH=/path/to/trackdub

dotnet test tests/DCS.Parser.CSharp.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
dotnet test tests/DCS.Runtime.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
dotnet test tests/DCS.Diff.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
dotnet test tests/DCS.Fix.Tests --filter "Category=CorpusGate&CorpusId=csharp-migration"
```

Legacy `TRACKDUB_PATH` is still supported.

**C# negative-control corpus (`csharp-negative-control`):**

```bash
git clone https://github.com/LykosAI/StabilityMatrix.git
cd StabilityMatrix && git checkout d97f6ccb9634a7ccfa7513be083aa70653112147
set CORPUS_CSHARP_NEGATIVE_CONTROL_PATH=C:\path\to\StabilityMatrix

dotnet test tests/DCS.Parser.CSharp.Tests --filter "Category=CorpusGate&CorpusId=csharp-negative-control"
```

**Java Spring corpus (`java-spring`):**

```bash
set CORPUS_JAVA_SPRING_PATH=/path/to/spring-petclinic
dotnet test tests/DCS.Parser.Java.Tests --filter "Category=CorpusGate&CorpusId=java-spring"
```

Legacy `PETCLINIC_PATH` is still supported.

CI checks out each corpus under `corpus/<id>/` and sets `DCS_CORPUS_PATH`. Private repos require the `CORPUS_CHECKOUT_PAT` repository secret (or legacy `TRACKDUB_PAT`).

---

## Releases

| Channel | How to install |
|---------|----------------|
| **Global tool (recommended)** | `dotnet tool install --global DependencyChainSubstrate.Cli` |
| **GitHub Releases** | Download `DependencyChainSubstrate.Cli.*.nupkg` from [Releases](https://github.com/tonythethompson/dependency-chain-substrate/releases), then `dotnet tool install --global --add-source . DependencyChainSubstrate.Cli` |
| **From source** | `dotnet pack src/DCS.Cli/DCS.Cli.csproj -c Release -o artifacts/nupkg` |

Version history: [CHANGELOG.md](CHANGELOG.md).

### Publishing a release (maintainers)

1. Bump `<Version>` in `src/DCS.Cli/DCS.Cli.csproj` and add a section to [CHANGELOG.md](CHANGELOG.md).
2. Commit, merge to `main`, then tag:
   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```
3. The [release workflow](.github/workflows/release.yml) runs on `v*` tags: unit tests → pack → GitHub Release (`.nupkg` attached) → NuGet.org push via **trusted publisher**.

**NuGet.org:** configure a trusted publisher on [nuget.org](https://www.nuget.org) for package `DependencyChainSubstrate.Cli` → GitHub → `tonythethompson/dependency-chain-substrate` → workflow **`release.yml`**. No `NUGET_API_KEY` secret required (OIDC via `--api-key az`).

**Manual draft release:** Actions → **release** → **Run workflow** with a version matching the csproj (creates a draft GitHub Release for smoke-testing).

---

## Development

```bash
dotnet restore
dotnet build
dotnet test
```

CI runs on push/PR to `main`:

| Workflow | Trigger |
|----------|---------|
| [`ci.yml`](../.github/workflows/ci.yml) | push, pull_request |
| [`release.yml`](../.github/workflows/release.yml) | push tag `v*`, manual dispatch |

CI job types on push/PR:

1. **build-test** (Windows, .NET 8) — unit and fixture tests, excluding corpus gates
2. **corpus-matrix** — loads gate definitions from [`ci/corpus-gates.json`](ci/corpus-gates.json)
3. **corpus-gate** — one matrix leg per configured ground-truth corpus (runner and SDK vary by gate)

See [`ci/README.md`](ci/README.md) for adding gates or running them locally.

### Agent and design discipline

This repo uses explicit **Designed → Implemented → Tested → Verified** gates. See [`AGENTS.md`](AGENTS.md) for agent workflow rules and [`PLAN.md`](PLAN.md) for milestone status.

Key design artifacts:

- [`docs/DESIGN.md`](docs/DESIGN.md) — problem statement, IR model, module specs
- [`docs/decisions/`](docs/decisions/) — ADRs for extraction strategy, identity, form factor, semantic resolution, and more
- [`planning/00-plan-of-plan.md`](planning/00-plan-of-plan.md) — origin story and phasing rationale

---

## Current status

Phases **0–13** and **Phase 9 (runtime overlay MVP)** are implemented as of 2026-06-30, including:

- Static C# extraction with semantic Roslyn resolution (~92% on Trackdub aggregate)
- Cross-TFM project-reference compilation closure
- Factory-lambda shallow dependency tracing
- Structured JSON analysis reports
- `dcs path` Path Excavator MVP
- Spring Boot parser (Phase 6)
- `dcs fix` DUPLICATE + ORPHANED + simple BROKEN preview/apply (Phase 8 / 8.1b / 8.1d)
- Optional `dcs fix --apply --verify-build` compile verification with rollback on build failure
- Runtime enrichment overlay — `DcsRuntimeEventListener` + `dcs enrich` (Phase 9 **Verified** on Trackdub @ pin)

**Parked / deferred:** IDE extension, TypeScript/Python parsers.

Track progress in [`PLAN.md`](PLAN.md).

---

## Contributing

1. Read [`AGENTS.md`](AGENTS.md) and the relevant ADRs before architectural changes.
2. Keep changes focused; do not advance milestone status without test evidence.
3. For corpus gate work, set the path env var from [`ci/corpus-gates.json`](ci/corpus-gates.json) or use legacy `TRACKDUB_PATH` / `PETCLINIC_PATH`.
4. Conventional commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`) are preferred.

Issues and pull requests are welcome.

---

## License

[MIT](LICENSE) © 2026 [tonythethompson](https://github.com/tonythethompson)
