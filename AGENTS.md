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

- `launch <agent> [--endpoint URL --protocol P --model M --persist config|model]`; `--endpoint` and `--protocol` are only valid together. The `config` surface is `show`, typed-key `get`/`set` (closed key set: `defaultBackend`; read-only `schemaVersion`/`backends`; `backends.<name>.endpoint|.protocol|.defaultModel`), and the nested `backend add|list|remove|set-key|clear-key` verbs - user-facing docs live in the README's Configuration section; the command sources remain the normative reference. `config set defaultBackend` is the sole default-pointer write path.
- **`launch <agent>` is wired through `LaunchCommand`'s own action, not the root's**: System.CommandLine runs the matched subcommand's action, and the root action can never read a child command's tokens — the root only handles the no-args wizard. `Program.cs` constructs the orchestrator and registers it on the subcommand; invoking `LaunchCommand` without that wiring is a loud failure, not a silent no-op. Orchestrator behavior itself is covered by `LaunchOrchestratorTests` (calls `RunAsync` directly); the parse-and-invoke wiring is covered by `LaunchCommandInvocationTests`.
- No-args invocation — and `launch` with no agent and no options — runs the interactive wizard (both entry points share `RunWizardAsync` in `Program.cs`; options without an agent still fail validation so the wizard fallback can't swallow a flag). The wizard is write-free: it returns a `WizardResult` and never saves settings. The wizard refuses unavailable harnesses at the harness step with the install hint and aborts (never re-prompts forever) when no available harness can be a valid pick. Wizard I/O goes through `IConsoleAbstraction`; tests enqueue scripted input via `TestConsole.EnqueueChoice/EnqueueText`. `ConsoleChoice.Annotation` is raw text — the presenter (`SpectreConsoleAbstraction`) owns the parenthesised rendering, so don't wrap it at the call site.
- Config-file writes must flow through the orchestrator + `BackupService`: backup before write, restore on exit unless `--persist config`; `SelfHealAsync` replays leftover backups at each launch (crash recovery). Harness env-var configuration rides on `LaunchPlan.Process.EnvironmentVariables` (child-process scope) — the launch path never mutates the launcher process env.
- Process launching uses **CliInvoke** (`ProcessConfiguration` + `CliRun.RunAsync`) — the repo migrated off `System.Diagnostics.Process`; keep it that way.

## Conventions & gotchas

- **Exit codes are a documented contract** (README table, enum in `Commands/LaunchExitCode.cs`: 0 and 2–10 — append-only). Don't renumber or add without updating README. Any unhandled exception *inside the orchestrator's launch flow* also returns 7 (ConfigRestoreFailure) — check stderr for `Launch failed:` before assuming a restore bug; user-rectifiable refusals (protocol mismatch → exit 3, Codex provider-block collision → exit 10) are separate classes that don't use that prefix. LaunchCommand's missing-orchestrator guard is the deliberate exception: unreachable in production wiring, it throws `InvalidOperationException` with no catch, surfacing as a raw unhandled exception — a loud wiring bug, not a user-facing exit code.
- Settings file is `HarnessGateway-launcher.json` in the per-OS config dir (`%APPDATA%\HarnessGateway` / `~/Library/Application Support/HarnessGateway` / `~/.config/HarnessGateway`). API keys live in the platform credential store behind `ISecureKeyStore` (`Core/Settings/Security/`); the JSON holds only opaque references — never write secret material into settings.
- Standing "we deliberately don't do this" refusals are documented in `docs/agents/out-of-scope.md` — check it before proposing a new flag, verb, or behavior. Historical design decisions are in git history, not in the working tree.
- Style is mixed: command/orchestration files use explicit types, adapters use `var` — match the file you're editing. File-scoped namespaces everywhere.
- Legacy names linger from the LmProxy → HarnessGateway rename: old `LmProxy.AgentLauncher.*` names persist in bin/obj artifacts and test-report filenames. Don't resurrect them.
- CI is three workflows in `.github/workflows/`: `ci.yml` (push to main + every PR — restore, Release build, full TUnit suite, new-Meziantou-warnings gate with an inline baseline, single Linux runner), `draft-release.yml` (push to main — dotnet-releaser `run` builds all six RIDs, four-RID `--version`/`--help` smoke test, creates/updates a *draft* release with the generated changelog, never publishes), and `publish-release.yml` (`workflow_dispatch` only — asserts the tag matches the csproj `<Version>`, `dotnet-releaser build` from the tag, attaches binaries to the draft, flips it to published). Artifact shape lives in `dotnet-releaser.toml` (every artifact-affecting value explicit; NuGet/brew/scoop/coverage/service disabled). Keep new commands lean — the plan is a unified CLI rename when proxy-server verbs arrive (see TODO.md).
