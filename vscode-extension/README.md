# DCS VS Code Extension (Phase 7 scaffold)

Thin VS Code consumer for **ADR-006**: spawns `dcs analyze --format json` and maps
`analysis-report-1.0.json` findings to editor diagnostics. Does **not** parse IR.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) and a built `dcs` CLI on `PATH`
  (or set `dcs.cliPath` in settings).
- Node.js 20+ for extension development.

Build the CLI from the repo root:

```bash
dotnet build src/DCS.Cli/DCS.Cli.csproj
# optional: dotnet tool install --add-source ./nupkg ...
```

## Develop

```bash
cd vscode-extension
npm install
npm run compile
```

Press **F5** in VS Code (Run Extension) with this folder open, or use **Run and Debug**
on a workspace containing a `.csproj`.

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `dcs.cliPath` | `dcs` | CLI executable |
| `dcs.analyzeOnOpen` | `true` | Warm analysis on workspace open |
| `dcs.analyzeOnSave` | `true` | Re-analyze on C# save |
| `dcs.cacheDir` | `""` | Optional `--cache-dir` |
| `dcs.strict` | `false` | Pass `--strict` to CLI |

## Commands

- **DCS: Analyze Workspace** (`dcs.analyze`)

## Public API contract

Report schema: `docs/schemas/analysis-report-1.0.json` (major version 1.x).
Extension rejects unsupported major versions.

## Phase 7 gate (not yet met)

- Inline LEAKED badge on Trackdub mid-migration commit within 5s of open
- Zero false-positive error diagnostics on `csharp-negative-control` corpus
