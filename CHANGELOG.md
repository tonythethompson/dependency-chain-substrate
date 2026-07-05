# Changelog

All notable changes to the `DependencyChainSubstrate.Cli` global tool are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.1] - 2026-07-05

### Fixed

- **CI corpus pin** — Trackdub pin moved to `b57fc832` (GitHub `main`); prior pin `5fd8b481` was local-only and broke corpus checkout in Actions.
- **Release validation** — align `src/DCS.Cli/DCS.Cli.csproj` `<Version>` with git tag before pushing `v*`.

### Added

- Leaked fix path and related tests (see commit history on `main`).

[0.1.1]: https://github.com/tonythethompson/dependency-chain-substrate/releases/tag/v0.1.1

## [0.1.0] - 2026-07-05

### Added

- **`dotnet tool` packaging** — install globally as `dcs` (`DependencyChainSubstrate.Cli` on NuGet when published).
- **`dcs fix --commit <sha> --preview`** — preview fixes against pinned commit source without checkout.
- **Parser exclusions** — `.claude/` and `.cursor/` agent worktree paths skipped during directory parse (aligns fix counts with corpus analyze).

### Fixed

- **Fix preview diffs** — registration removal no longer reformats entire files; single-line diffs for duplicate/orphan removals.
- **Multi-edit same file** — bulk `--all-duplicates` and orphaned preview no longer fail after the first removal in a large file (e.g. `compositionroot.cs`).
- **`TryAddSingleton<T>(lambda)`** — statement locator handles concrete singleton factory registrations.

### Notes

- **`dcs fix --apply`** still writes the working tree only; use `--commit` with `--preview` to match CI pin analysis.
- **Broken-chain auto-fix** remains limited to shallow factory lambdas without `GetRequiredService`.
- **Trackdub corpus pin** for gates: `b57fc832` (see `tests/verification/TrackdubPin.cs`).

[0.1.0]: https://github.com/tonythethompson/dependency-chain-substrate/releases/tag/v0.1.0
