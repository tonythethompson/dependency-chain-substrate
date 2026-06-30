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
| **Auto-fix (preview)** | `dcs fix` | Safe preview/apply of DUPLICATE registration removal (C# only, v1) |

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

### Run the CLI

```bash
dotnet run --project src/DCS.Cli -- analyze /path/to/your/repo
```

During development, prefix commands with `dotnet run --project src/DCS.Cli --` (or build once and invoke `src/DCS.Cli/bin/Debug/net8.0/dcs.exe` directly).

### Analyze a specific commit

```bash
dotnet run --project src/DCS.Cli -- analyze /path/to/repo --commit abc1234
```

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
| `viz <repo>` | Generate self-contained HTML visualization |
| `fix <repo>` | Preview/apply DUPLICATE removal (C# working tree) |

### Common options

| Flag | Applies to | Description |
|------|------------|-------------|
| `--commit <sha>` | analyze, atlas, dump-ir, path, viz | Analyze a specific git commit (blob read) |
| `--language auto\|csharp\|java` | analyze, diff, atlas, viz | Parser selection |
| `--context <id>` | analyze, path | Select TFM/context (`all` for summary) |
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
| `--preview` / `--apply` | fix | Preview diff vs write changes |

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
| `PLAN.md` | Milestone tracker and phase gates |

---

## Languages and scope

### In scope (today)

- **C#** — `Microsoft.Extensions.DependencyInjection` call-site patterns; semantic type resolution per TFM; built-in framework tags (WinUI, Avalonia, WPF, ASP.NET Core, etc.)
- **Java / Spring Boot** — `@Bean`, stereotypes, `@Autowired` wiring; PetClinic integration gate

### Explicitly out of scope (v1)

- Runtime container profiling (planned Phase 9)
- IDE extension (deferred Phase 7)
- TypeScript / Python parsers (parked)
- Auto-fix beyond DUPLICATE removal
- Full semantic MSBuildWorkspace dependency on project build

---

## Ground truth and verification

DCS is validated against real migration corpora, not toy fixtures alone.

| Corpus | Role | Gate |
|--------|------|------|
| **Trackdub** | Private WinUI→Avalonia mid-migration reference | `TrackdubSemanticGateTests` — semantic resolution ≥85%, VoiceClone duplicate sites, Avalonia shell factory |
| **Spring PetClinic** | Java/Spring IR smoke test | `SpringPetClinicIntegrationTests` |
| **di-patterns fixtures** | Parser pattern catalog regression | Golden CLI/JSON tests |

Trackdub pin: `3c4e374d23fe3941ed7ca376775937941973b313`

### Running corpus gates locally

**Trackdub (C#):**

```bash
# Clone Trackdub and point at it
git clone https://github.com/tonythethompson/Trackdub.git
set TRACKDUB_PATH=A:\Trackdub   # Windows
# or: export TRACKDUB_PATH=/path/to/Trackdub

dotnet test tests/DCS.Parser.CSharp.Tests --filter "FullyQualifiedName~TrackdubSemanticGateTests"
```

CI clones Trackdub into `dcs-trackdub-pin/` using the `TRACKDUB_PAT` repository secret.

**PetClinic (Java):**

```bash
dotnet test tests/DCS.Parser.Java.Tests --filter "FullyQualifiedName~SpringPetClinicIntegrationTests"
```

---

## Development

```bash
dotnet restore
dotnet build
dotnet test
```

CI runs three jobs on push/PR:

1. **build-test** (Windows, .NET 8) — full test suite excluding optional corpus gates
2. **trackdub-semantic** (Windows, .NET 10) — Trackdub semantic gate at pin SHA
3. **petclinic-linux** (Ubuntu, .NET 8) — Spring PetClinic gate

See [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

### Agent and design discipline

This repo uses explicit **Designed → Implemented → Tested → Verified** gates. See [`AGENTS.md`](AGENTS.md) for agent workflow rules and [`PLAN.md`](PLAN.md) for milestone status.

Key design artifacts:

- [`docs/DESIGN.md`](docs/DESIGN.md) — problem statement, IR model, module specs
- [`docs/decisions/`](docs/decisions/) — ADRs for extraction strategy, identity, form factor, semantic resolution, and more
- [`planning/00-plan-of-plan.md`](planning/00-plan-of-plan.md) — origin story and phasing rationale

---

## Current status

Phases **0–13** are closed as of 2026-06-30, including:

- Static C# extraction with semantic Roslyn resolution (~92% on Trackdub aggregate)
- Cross-TFM project-reference compilation closure
- Factory-lambda shallow dependency tracing
- Structured JSON analysis reports
- `dcs path` Path Excavator MVP
- Spring Boot parser (Phase 6)
- `dcs fix` DUPLICATE preview/apply (Phase 8)

**Parked / deferred:** IDE extension, runtime enrichment overlay, TypeScript/Python parsers, viz path highlighting.

Track progress in [`PLAN.md`](PLAN.md).

---

## Contributing

1. Read [`AGENTS.md`](AGENTS.md) and the relevant ADRs before architectural changes.
2. Keep changes focused; do not advance milestone status without test evidence.
3. For Trackdub gate work, ensure `TRACKDUB_PATH` or `dcs-trackdub-pin/` is at the pin SHA.
4. Conventional commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`) are preferred.

Issues and pull requests are welcome.

---

## License

[MIT](LICENSE) © 2026 [tonythethompson](https://github.com/tonythethompson)
