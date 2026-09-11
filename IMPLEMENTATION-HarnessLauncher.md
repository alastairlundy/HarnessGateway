# Implementation Blueprint — HarnessLauncher CLI

## Scope Binding

- Linked Spec: `docs/decisions/DECISIONS-LmProxy-agent-launcher.md` (no separate spec file exists — the ledger's session-goal record `D001` is the working spec)
- Decision Ledger: `docs/decisions/DECISIONS-LmProxy-agent-launcher.md`
- Notice: this blueprint is a context pointer valid ONLY for the linked spec and must not be applied to other specifications without explicit authorization.

## Ledger Reference

Cited in this blueprint: [`DECISIONS-LmProxy-agent-launcher.md#D001`], [`#D002`], [`#D003`], [`#D004`], [`#D005`], [`#D006`], [`#D007`], [`#D008`], [`#D009`], [`#D010`], [`#D011`], [`#D012`], [`#D013`], [`#D014`], [`#D015`], [`#D016`], [`#D017`], [`#D018`], [`#D019`], [`#D020`], [`#D021`], [`#T001`], [`#T002`], [`#T003`], [`#T004`], [`#T005`], [`#T006`], [`#T007`], [`#T008`], [`#T009`], [`#T010`], [`#T011`], [`#T012`], [`#T013`], [`#T014`], [`#T015`], [`#T016`], [`#T017`], [`#T018`], [`#T019`], [`#T020`], [`#T021`], [`#T022`], [`#T023`], [`#T024`] — all in `DECISIONS-LmProxy-agent-launcher.md`.

## Product summary

A .NET 10 console CLI, working root command name `lmlaunch`, exposing `launch` (interactive wizard) and `launch <agent>` (direct), plus `config`, that configures and starts agent harnesses in the current working directory against a user-selected Backend (OpenAI Chat Completions, OpenAI Responses, or Anthropic protocol) [`DECISIONS-LmProxy-agent-launcher.md#D001`], [`#D002`], [`#D004`], [`#D016`], [`#T010`]. A future unified CLI will own proxy-server verbs; the rename trigger is recorded in the ledger [`#D017`].

## 1. Solution: `src/LmProxy.slnx`

Add two projects to the existing solution: `LmProxy.AgentLauncher.Core` (class library) and `LmProxy.AgentLauncher.Tests` (test project) [`#T002`].

## 2. `src/LmProxy.AgentLauncher.Core/` (new class library, net10.0)

Dependencies: CliInvoke 3.0.0-beta.3 and Microsoft.Extensions.Configuration packages as already locked in the csproj [`#D003`]. No Spectre.Console here — console UI stays in the app project [`#T004`].

### `Backends/ProtocolKind.cs`
Enum with `OpenAiChat`, `OpenAiResponses`, `Anthropic`, mapping 1:1 to `--protocol` values `openai-chat`, `openai-responses`, `anthropic` [`#T017`], [`#D021`].

### `Backends/Backend.cs`
Sealed record `(string Name, Uri BaseUri, ProtocolKind Protocol, string? DefaultModel, string? ApiKey)` [`#T018`]. `ApiKey` is plaintext JSON in v1; keychain/DPAPI hardening is deferred [`#D012`].

### `Settings/LauncherSettings.cs`
Record `(List<Backend> Backends, string DefaultBackend, int SchemaVersion)` [`#T021`]. v1 requires exactly one entry; the list-plus-pointer shape is profile-ready but profile verbs are out of scope for v1 [`#T011`].

### `Settings/SettingsStore.cs`
JSON file in the platform config directory, loaded through Microsoft.Extensions.Configuration with JSON + environment-variable layering (env vars override file values) [`#T006`]. Settings writes originate only from `config` subcommands or explicit `--persist model` on non-interactive launches — never from the wizard [`#T016`], [`#T013`].

### `Adapters/HarnessMetadata.cs` + `Adapters/HarnessKind.cs`
Record `(string Name, HarnessKind Kind, bool RequiresModel, string InstallHint, string? DetectionCommand, IReadOnlyList<string> ConfigFileCandidates)` plus a `Cli | Gui` enum [`#T019`]. Null `DetectionCommand` means probe the launch command on PATH [`#T019`]; `Kind` covers GUI harnesses [`#D020`].

### `Adapters/IHarnessAdapter.cs`
Interface: `Metadata`, `IsAvailableAsync(ct)`, `PlanLaunchAsync(LaunchRequest, ct) → LaunchPlan` [`#T020`]. Adapters are pure planners; Core executes [`#T020`].

### `Adapters/OpenCodeAdapter.cs`, `KiloAdapter.cs`, `CopilotCliAdapter.cs`, `PiAdapter.cs`
Four v1 adapters under feature folder `Adapters/` [`#D005`], [`#T001`]. Each declares accurate metadata (model requirement per harness behavior [`#D014`], install hint [`#D018`]) and plans the D010 hybrid: env vars/CLI args where the harness honors them, minimal backed-up config-file writes otherwise [`#D010`]. Adapters must handle both CLI and GUI kinds for detection and launch mechanics [`#D020`], and undeclared-model behavior defaults to warn-and-launch [`#D014`].

### `Configuration/ConfigurationAction.cs`
Closed family: abstract `ConfigurationAction` with `CliArgumentAction(Argument, Value?)`, `ConfigFileWriteAction(TargetPath, Content)`, `EnvironmentVariableAction(Name, Value)` [`#T023`]. Quirks that cannot fit these three variants re-open the family [`#T023`].

### `Configuration/BackupService.cs`
Before the first config-file write in a launch, back up each target (path + original bytes + `persisted` marker) in one atomic file operation [`#D010`], [`#T008`]. On harness exit restore the originals — the launcher never causes lasting damage by default [`#D011`] — unless `--persist config` was given [`#T015`]. At the start of every launch, self-heal: restore leftover backups whose marker is NOT `persisted`; deliberately persisted backups are skipped [`#T008`], [`#T015`]. Ctrl+C/TERM handlers restore before exiting [`#T008`].

### `Execution/LaunchRunner.cs`
Executes the plan's `ProcessConfiguration` directly via CliInvoke — no ProcessStartInfo round-trip [`#T024`] — in the foreground with inherited stdio, exiting with the harness's exit code (matches `ollama launch`) [`#D015`].

### `Backends/Models/ModelCatalogClient.cs`
Hand-rolled per-protocol `HttpClient` calls (OpenAI `/v1/models`, shared by both OpenAI protocols; Anthropic `/v1/models`) normalizing responses into the catalog; JSON shapes maintained by hand [`#T007`]. Degrades gracefully when the endpoint is unreachable [`#D013`].

### `Backends/Models/ModelCatalog.cs`
`ModelCatalogEntry(string Id, string? DisplayName)` and `ModelCatalog(IReadOnlyList<ModelCatalogEntry> Models, DateTimeOffset FetchedAt)` [`#T022`].

### `Launch/LaunchRequest.cs` / `Launch/LaunchPlan.cs`
`LaunchRequest(Backend, string? Model, string WorkingDirectory)`; `LaunchPlan(ProcessConfiguration Process, IReadOnlyList<ConfigurationAction> ConfigurationActions)` [`#T024`]. Launch types are coupled to CliInvoke 3.0.0-beta.3's API shape; beta churn re-opens `T024` [`#T024`].

## 3. `src/LmProxy.AgentLauncher/` (app project)

Dependencies add Spectre.Console for rendering and prompts; parsing stays System.CommandLine [`#T004`], [`#D003`].

### `Program.cs`
Root command with `launch` and `config` subcommands [`#D016`]; working root name `lmlaunch` until the D017 rename [`#D017`].

### `Commands/LaunchCommand.cs`
- `launch` (no args) runs the wizard; `launch <agent>` runs direct [`#D004`]. Both act on the CWD [`#D004`].
- Long flags: `--endpoint <url>`, `--protocol <openai-chat|openai-responses|anthropic>`, `--model <id>`, `--persist <config|model>` (repeatable, comma-list sugar accepted) [`#T010`], [`#T015`]. Raw-URL overrides require both `--endpoint` and `--protocol`; no protocol probing on the launch path [`#T010`].
- `--persist model` saves the chosen model as the per-backend default model in settings [`#T015`], [`#T014`]; `--persist config` suppresses restore-on-exit for that launch's file writes [`#T015`], [`#D011`].
- Exit codes per class: 0 ok; 2 usage; 3 unknown agent; 4 harness unavailable; 5 endpoint unreachable; 6 required model missing; 7 config/restore failure [`#T012`].

### `Commands/LaunchOrchestrator.cs`
Pipeline order: load settings → resolve backend (saved default at zero flag cost, or explicit override) [`#D009`], [`#T011`] → self-heal leftover unmarked backups [`#T008`] → availability check; wizard marks unavailable harnesses, direct mode fails with the adapter's install hint before any config action [`#D018`] → adapter plans [`#T020`] → Core executes configuration actions with backup [`#D010`], [`#T008`] → foreground run to exit [`#D015`] → restore unless persisted [`#D011`], [`#T015`].

### `Commands/ConfigCommand.cs`
Verbs: show/set default endpoint (+protocol), set/clear saved key, set default model — the only interactive-path writers, plus `--persist model` from direct launch [`#T006`], [`#D016`], [`#T014`], [`#T016`].

### `Wizard/WizardPipeline.cs`
Step order: harness → endpoint (prefilled from the saved default; a change applies once for this launch) → key (only when needed and unsaved; asks but never stores) → model [`#D019`], [`#T013`]. Unavailable harnesses are annotated in the harness list [`#D018`]. The wizard never writes settings [`#T013`].

### `Wizard/ModelStep.cs`
Picker populated from the catalog with an explicit skip option; skip is blocked when the adapter's `RequiresModel` is true and warned about in direct mode [`#D013`], [`#D014`]. Prefill the saved per-backend default model when one exists [`#T014`]. When the endpoint is unreachable the step degrades gracefully (skip or manual entry) rather than failing the launch [`#D013`].

## 4. `tests/LmProxy.AgentLauncher.Tests/` (new, TUnit)

TUnit with fake-based tests: temp-dir config writes, stub HTTP handlers, scripted fake harnesses [`#T009`], [`#T002`].
- Settings round-trip and env-var-over-file precedence [`#T006`], [`#T009`].
- Backup/restore: fake harness runs and exits → original config restored; exit codes propagate [`#D011`], [`#T008`], [`#D015`].
- Persist path: `--persist config` leaves writes plus marker; kill simulation leaves a marked backup that self-heal does NOT undo [`#T015`].
- Self-heal: leftover unmarked backup restored at the next launch before new config actions [`#T008`].
- ModelCatalogClient per protocol via stub handler; unreachable endpoint degrades [`#T007`], [`#D013`], [`#T009`].
- Adapter planning against temp dirs; wizard step logic with a testable console [`#T009`], [`#T004`].
- Real-harness integration matrix is a later add-on [`#T009`].

## 5. Packaging

Each release publishes a NuGet dotnet tool package and per-OS GitHub Release binaries [`#T003`]. Tool id and binary names use the working root name until the `D017` rename [`#T017`→`#D017`], [`#T003`].

## 6. Deliberately deferred (do not build in v1)

- Credential storage hardening (keychain/DPAPI) [`#D012`], [`#T018`].
- Backend profile verbs (add/switch/list) [`#T011`].
- Adapters for Codex, Claude Code, OpenClaw, Hermes, Cline [`#D007`].
- Root-command rename and proxy-server verbs [`#D017`].
- Real-harness integration test matrix [`#T009`].
