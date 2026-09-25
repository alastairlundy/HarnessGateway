# AGENTS.md — HarnessGateway

CLI (`harnessgateway`) that launches agent harnesses (OpenCode, Kilo, Copilot CLI, Pi) pointed at a chosen backend (OpenAI/Anthropic-compatible endpoint). The launcher currently owns the whole codebase — don't invent a Proxy project yet.

## Build & test

- Solution is `src/HarnessGateway.slnx`, targeting **net10.0**. Use `--solution`, not a repo-root `.sln` or positional project path.
- Tests run on **native Microsoft Testing.Platform** (`global.json` pins `test.runner`), framework is **TUnit** (not xUnit/NUnit):

  ```bash
  dotnet test --solution src/HarnessGateway.slnx
  dotnet test --solution src/HarnessGateway.slnx --no-build --treenode-filter "/*/*/SkeletonTests/*"   # one class
  ```

  No `--` bridge separator (that's VSTest-mode syntax and fails here). TUnit class filtering is `--treenode-filter`; `--filter-class` fails with MTP exit code 5.
- Assertions are `await Assert.That(...)`. Known pre-existing warnings — don't add more of the same: `TUnitAssertions0005` (constant value in `Assert.That`, e.g. `LaunchExitCodeTests`), CS0618 for obsolete `HasCount()` (use `Count()`).
- Package versions come from CPM (`src/Directory.Packages.props`) — never add `Version=` on a `PackageReference`; add a `PackageVersion` there instead.

## Structure

- `HarnessGateway.Launcher` — `OutputType=Exe`, the dotnet tool (`PackageId=harnessgateway`, `ToolCommandName=harnessgateway`). Entry: `Program.cs` + `Commands/` (System.CommandLine) + `Wizard/` (Spectre.Console).
- `HarnessGateway.Launcher.Core` — domain logic (Adapters, Backends, Settings, Launch, Execution, Configuration). Referenced by the exe; both are `InternalsVisibleTo` the tests.
- Per-harness behavior lives in `Core/Adapters/*Adapter.cs`, one class per harness: detection, config actions, launch plan. Adding a harness = new adapter class + add it to the `List<IHarnessAdapter>` in `Program.cs` + tests (`HarnessKind` already has `Cli` and `Gui`).
- Agent lookup is case-insensitive equality on `Metadata.Name` ("OpenCode", "Kilo", "GitHub Copilot CLI", "Pi") — not the short slugs in `LaunchCommand`'s help text; `launch copilot` will not match.

## CLI surface & gotchas

- `launch <agent> [--endpoint URL --protocol P --model M --persist config|model]`; `--endpoint` and `--protocol` are only valid together. The `config` verbs (`show`, `set-default-endpoint`, `set-saved-key`, `clear-saved-key`, `set-default-model`) are documented only in `ConfigCommand.cs`, not the README.
- **`harnessgateway launch <agent>` via the built binary currently no-ops** (silent exit 0, verified): the `launch` subcommand has no action of its own and the root action can't read its argument. Verify launch behavior through TUnit tests — `LaunchOrchestratorTests` calls `RunAsync` directly.
- No-args invocation runs the interactive wizard. The wizard is write-free: it returns a `WizardResult` and never saves settings. Wizard I/O goes through `IConsoleAbstraction`; tests enqueue scripted input via `TestConsole.EnqueueChoice/EnqueueText`.
- Config-file writes must flow through the orchestrator + `BackupService`: backup before write, restore on exit unless `--persist config`; `SelfHealAsync` replays leftover backups at each launch (crash recovery). Env-var actions are plain process env sets.
- Process launching uses **CliInvoke** (`ProcessConfiguration` + `CliRun.RunAsync`) — the repo migrated off `System.Diagnostics.Process`; keep it that way.

## Conventions & gotchas

- **Exit codes are a documented contract** (README table, enum in `Commands/LaunchExitCode.cs`: 0/2/3/4/5/6/7). Don't renumber or add without updating README. Any unhandled exception in the launch flow also returns 7 (ConfigRestoreFailure) — check stderr for `Launch failed:` before assuming a restore bug.
- Settings file is `HarnessGateway-launcher.json` in the per-OS config dir (`%APPDATA%\HarnessGateway` / `~/Library/Application Support/HarnessGateway` / `~/.config/HarnessGateway`). `ApiKey` is stored in plaintext — hardcoded TODO for keychain/DPAPI (TODO.md Security section); don't make it worse.
- Design decisions are locked and recorded in `docs/decisions/DECISIONS-LmProxy-agent-launcher.md` (D/T record IDs); `TODO.md` and `IMPLEMENTATION-TODO.md` cite them. `IMPLEMENTATION-TODO.md` is the planned Phase 2 (Codex/Claude Code adapters, `--backend` resolution, secure key store) — don't build it uninvited.
- Style is mixed: command/orchestration files use explicit types, adapters use `var` — match the file you're editing. File-scoped namespaces everywhere.
- Legacy names linger from the LmProxy → HarnessGateway rename: old `LmProxy.AgentLauncher.*` names persist in bin/obj artifacts, test-report filenames, and the decision-ledger title. Don't resurrect them.
- CI is release-only: `.github/workflows/release.yml` on `v*` tags builds per-OS `harnessgateway-<rid>.zip/.tar.gz` archives plus the NuGet tool package; there is no PR build workflow (just Dependabot). Keep new commands lean — the plan (TODO `#D017`) is a unified CLI rename when proxy-server verbs arrive.
