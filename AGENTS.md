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

- `HarnessGateway.Launcher` — `OutputType=Exe`, distribution is per-OS binaries only. Entry: `Program.cs` + `Commands/` (System.CommandLine) + `Wizard/` (Spectre.Console).
- `HarnessGateway.Launcher.Core` — domain logic (Adapters, Backends, Settings, Launch, Execution, Configuration). Referenced by the exe; both are `InternalsVisibleTo` the tests.
- Per-harness behavior lives in `Core/Adapters/*Adapter.cs`, one class per harness: detection, config actions, launch plan. Adding a harness = new adapter class + add it to the `List<IHarnessAdapter>` in `Program.cs` + tests (`HarnessKind` already has `Cli` and `Gui`).
- `HarnessMetadata.Name` is a **display label** ("GitHub Copilot CLI") and is **not** an input token. `CommandName` is the token (`copilot`); `Aliases` adds extra accepted spellings (`claude`, `github-copilot-cli`). Agent lookup goes through `HarnessLookup.Resolve`, which matches `CommandName` + `Aliases` case-insensitively and never `Name` — so `launch copilot` works and `launch "GitHub Copilot CLI"` exits 3 by design. `LaunchCommand`'s agent help text is **generated** from the registry (`BuildAgentDescription`), never hand-written; `Shipped_adapters_resolve_their_own_command_names` gates that invariant. The wizard lists `Name` but returns `CommandName` in `WizardResult.AgentCommandName`, so the launch path only ever sees tokens.
- "Did you mean" hints on both the agent and backend paths share `Core/Text/CloseMatch.cs` (prefix, then Levenshtein within 2). Non-contractual UX: tests must not pin hint text.

## CLI surface & gotchas

- `launch <agent> [--endpoint URL --protocol P --model M --persist config|model]`; `--endpoint` and `--protocol` are only valid together. The `config` verbs (`show`, `set-default-endpoint`, `set-saved-key`, `clear-saved-key`, `set-default-model`) are documented only in `ConfigCommand.cs`, not the README.
- **`launch <agent>` is wired through `LaunchCommand`'s own action, not the root's**: System.CommandLine runs the matched subcommand's action, and the root action can never read a child command's tokens — the root only handles the no-args wizard. `Program.cs` constructs the orchestrator and registers it on the subcommand; invoking `LaunchCommand` without that wiring is a loud failure, not a silent no-op. Orchestrator behavior itself is covered by `LaunchOrchestratorTests` (calls `RunAsync` directly); the parse-and-invoke wiring is covered by `LaunchCommandInvocationTests`.
- No-args invocation — and `launch` with no agent and no options — runs the interactive wizard (both entry points share `RunWizardAsync` in `Program.cs`; options without an agent still fail validation so the wizard fallback can't swallow a flag). The wizard is write-free: it returns a `WizardResult` and never saves settings. The wizard refuses unavailable harnesses at the harness step with the install hint and aborts (never re-prompts forever) when no available harness can be a valid pick — D023 amends D018's annotate-only wizard half. Wizard I/O goes through `IConsoleAbstraction`; tests enqueue scripted input via `TestConsole.EnqueueChoice/EnqueueText`. `ConsoleChoice.Annotation` is raw text — the presenter (`SpectreConsoleAbstraction`) owns the parenthesised rendering, so don't wrap it at the call site.
- Config-file writes must flow through the orchestrator + `BackupService`: backup before write, restore on exit unless `--persist config`; `SelfHealAsync` replays leftover backups at each launch (crash recovery). Harness env-var configuration rides on `LaunchPlan.Process.EnvironmentVariables` (child-process scope, T045) — the launch path never mutates the launcher process env.
- Process launching uses **CliInvoke** (`ProcessConfiguration` + `CliRun.RunAsync`) — the repo migrated off `System.Diagnostics.Process`; keep it that way.

## Conventions & gotchas

- **Exit codes are a documented contract** (README table, enum in `Commands/LaunchExitCode.cs`: 0/2/3/4/5/6/7/10; the ledger reserves 8 for unknown-backend-name and 9 for schema-version refusal — append-only). Don't renumber or add without updating README. Any unhandled exception in the launch flow also returns 7 (ConfigRestoreFailure) — check stderr for `Launch failed:` before assuming a restore bug; user-rectifiable refusals (protocol mismatch per T028 → exit 3, Codex provider-block collision per T037 → exit 10) are separate classes that don't use that prefix.
- Settings file is `HarnessGateway-launcher.json` in the per-OS config dir (`%APPDATA%\HarnessGateway` / `~/Library/Application Support/HarnessGateway` / `~/.config/HarnessGateway`). `ApiKey` is stored in plaintext — hardcoded TODO for keychain/DPAPI (TODO.md Security section); don't make it worse.
- Design decisions are locked and recorded in `docs/decisions/DECISIONS-LmProxy-agent-launcher.md` (D/T record IDs); `TODO.md` and `IMPLEMENTATION-TODO.md` cite them. `IMPLEMENTATION-TODO.md` is the planned Phase 2 (Codex/Claude Code adapters, `--backend` resolution, secure key store) — don't build it uninvited.
- Style is mixed: command/orchestration files use explicit types, adapters use `var` — match the file you're editing. File-scoped namespaces everywhere.
- Legacy names linger from the LmProxy → HarnessGateway rename: old `LmProxy.AgentLauncher.*` names persist in bin/obj artifacts, test-report filenames, and the decision-ledger title. Don't resurrect them.
- CI is release-only: `.github/workflows/release.yml` on `v*` tags builds per-OS `harnessgateway-<rid>.zip/.tar.gz` archives; there is no PR build workflow (just Dependabot). Keep new commands lean — the plan (TODO `#D017`) is a unified CLI rename when proxy-server verbs arrive.
