# IMPLEMENTATION-TODO.md — HarnessGateway.Launcher Phase 2

## Scope Binding

- **Linked Spec**: `TODO.md` (repo root)
- **Decision Ledger**: `docs/decisions/DECISIONS-LmProxy-agent-launcher.md`
- This blueprint is a context pointer valid ONLY for the linked spec and must not be applied to other specifications without explicit authorization.
- Ledger-era project names (`LmProxy.AgentLauncher`) translate to the current `HarnessGateway.Launcher` / `HarnessGateway.Launcher.Core` projects; decision content is unchanged.
- Locked foundation reused without re-grilling: adapter pattern [`DECISIONS-LmProxy-agent-launcher.md#T005`], metadata record [`DECISIONS-LmProxy-agent-launcher.md#T019`], plan/execute split [`DECISIONS-LmProxy-agent-launcher.md#T020`], closed action family [`DECISIONS-LmProxy-agent-launcher.md#T023`], backend model [`DECISIONS-LmProxy-agent-launcher.md#D008`], protocol enum [`DECISIONS-LmProxy-agent-launcher.md#T017`], exit-code table [`DECISIONS-LmProxy-agent-launcher.md#T012`], restore/self-heal [`DECISIONS-LmProxy-agent-launcher.md#T008`], persistence scope [`DECISIONS-LmProxy-agent-launcher.md#T016`].

## Workstream A — Adapters (Codex, Claude Code, warn-and-launch)

### `src/HarnessGateway.Launcher.Core/Adapters/CodexAdapter.cs`

- New adapter class implementing `IHarnessAdapter` per the locked pattern [`DECISIONS-LmProxy-agent-launcher.md#T005`]/[`DECISIONS-LmProxy-agent-launcher.md#T020`].
- `HarnessMetadata`: Name `codex`, Kind `Cli`, `RequiresModel = true`, `SupportedProtocols = [openai-responses]`, `ConfigFileCandidates = [$CODEX_HOME/config.toml, ~/.codex/config.toml]`, DetectionCommand probing `codex` on PATH, InstallHint for npm/brew [`DECISIONS-LmProxy-agent-launcher.md#T025`], [`DECISIONS-LmProxy-agent-launcher.md#T044`].
- `PlanLaunchAsync` emits exactly: one `ConfigFileWriteAction` writing a `[model_providers.harnessgateway]` block (fields: `name`, `base_url`, `env_key`, `wire_api = "responses"`) plus top-level `model_provider` and `model` keys; plus one `EnvironmentVariableAction` injecting the saved key under the `env_key`-named variable [`DECISIONS-LmProxy-agent-launcher.md#T025`], [`DECISIONS-LmProxy-agent-launcher.md#T023`].
- TOML writes must round-trip comments/structure; resolve the real config path honoring a user-set `CODEX_HOME` [`DECISIONS-LmProxy-agent-launcher.md#T025`].
- Pre-write collision check: if a `[model_providers.harnessgateway]` block already exists, fail before any write [`DECISIONS-LmProxy-agent-launcher.md#T037`].
- Key handling: inject the saved key when present; launch without one when absent; never a dummy value [`DECISIONS-LmProxy-agent-launcher.md#D012`].
- Existing restore/self-heal (backup markers) applies unchanged to the config write; `--persist config` keeps the writes [`DECISIONS-LmProxy-agent-launcher.md#T008`], [`DECISIONS-LmProxy-agent-launcher.md#T015`].

### `src/HarnessGateway.Launcher.Core/Adapters/ClaudeCodeAdapter.cs`

- `HarnessMetadata`: Name `claude-code`, Kind `Cli`, `RequiresModel = false`, `SupportedProtocols = [anthropic]`, no ConfigFileCandidates, DetectionCommand probing `claude` on PATH, npm install hint [`DECISIONS-LmProxy-agent-launcher.md#T026`], [`DECISIONS-LmProxy-agent-launcher.md#T044`].
- `PlanLaunchAsync` emits only `EnvironmentVariableAction`s — `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN` (when a key is saved), `ANTHROPIC_MODEL` (when a model is chosen) — plus an optional `--model` `CliArgumentAction`; never config-file writes [`DECISIONS-LmProxy-agent-launcher.md#T026`], [`DECISIONS-LmProxy-agent-launcher.md#T023`].
- `ANTHROPIC_AUTH_TOKEN` is used over `ANTHROPIC_API_KEY` (the latter triggers Claude Code's interactive approval prompt) [`DECISIONS-LmProxy-agent-launcher.md#T026`].
- Document in the install hint: a non-first-party `ANTHROPIC_BASE_URL` disables MCP tool search and Remote Control [`DECISIONS-LmProxy-agent-launcher.md#T026`].

### Cross-adapter behavior

- Warn-and-launch: when model-requirement cannot be determined, direct mode prints a warning and launches; wizard warns and treats the harness as skippable; no new exit code [`DECISIONS-LmProxy-agent-launcher.md#T027`], [`DECISIONS-LmProxy-agent-launcher.md#D014`].
- Protocol-compat enforcement: if the resolved backend's protocol is not in the harness's `SupportedProtocols`, direct mode fails before any configuration action with exit code 3; the README code-3 label broadens to "unknown agent / invalid agent-backend selection" [`DECISIONS-LmProxy-agent-launcher.md#T028`], [`DECISIONS-LmProxy-agent-launcher.md#T012`].
- Adapter registration adds `codex` and `claude-code` to the `HarnessKind` registry; v1 harnesses (OpenCode, Kilo, Copilot CLI, Pi) unchanged [`DECISIONS-LmProxy-agent-launcher.md#D007`].

## Workstream B — Profile / backend surface (Core + Commands)

### `src/HarnessGateway.Launcher.Core/Backends/` (resolution)

- Direct `launch` accepts `--backend <name>` resolving against the saved backends list; `--endpoint`/`--protocol`/`--model` field-by-field override the resolved entry; raw-URL rule still requires `--endpoint` + `--protocol` together; no protocol probing [`DECISIONS-LmProxy-agent-launcher.md#T029`], [`DECISIONS-LmProxy-agent-launcher.md#T010`].
- No `--backend` given → resolve `DefaultBackend` pointer; wizard prefills endpoint and default model from that entry [`DECISIONS-LmProxy-agent-launcher.md#T033`], [`DECISIONS-LmProxy-agent-launcher.md#D019`], [`DECISIONS-LmProxy-agent-launcher.md#T014`].
- Keys are NOT overridable per launch — only `--endpoint/--protocol/--model` layer; key sourcing is the resolved entry's secure-store key only [`DECISIONS-LmProxy-agent-launcher.md#T036`].
- Unknown backend name (on `--backend`, `switch`, `remove`) → exit code 8 + "unknown backend name '<name>'" message; close-match "did you mean" hints are best-effort and non-contractual [`DECISIONS-LmProxy-agent-launcher.md#T031`], [`DECISIONS-LmProxy-agent-launcher.md#T038`].
- Protocol-incompat pre-check reuses exit code 3 (see Workstream A) before any config action [`DECISIONS-LmProxy-agent-launcher.md#T028`].
- `--persist` family stays closed at `config|model`; no persist type for backend selection [`DECISIONS-LmProxy-agent-launcher.md#T042`], [`DECISIONS-LmProxy-agent-launcher.md#T015`].

### `src/HarnessGateway.Launcher/Commands/BackendCommands.cs` (new)

- `config backend add <name> [--endpoint --protocol] [--key] ...`: creates or replaces an entry; writes settings (non-interactive scope) [`DECISIONS-LmProxy-agent-launcher.md#T030`], [`DECISIONS-LmProxy-agent-launcher.md#T016`].
- Key input: hidden terminal prompt when no `--key` given; `--key <secret>` for scripts; key stored only in the secure store; JSON holds the reference [`DECISIONS-LmProxy-agent-launcher.md#T035`], [`DECISIONS-LmProxy-agent-launcher.md#T040`].
- `config backend switch <name>`: validates existence, writes `DefaultBackend`, prints confirmation; idempotent on already-default; no entry mutation [`DECISIONS-LmProxy-agent-launcher.md#T032`].
- `config backend list`: read-only; entries + default marker [`DECISIONS-LmProxy-agent-launcher.md#T030`].
- `config backend remove <name>`: refuses the current default (require `switch` first) and refuses the final remaining entry [`DECISIONS-LmProxy-agent-launcher.md#T033`].

## Workstream C — Settings & secure store

### `src/HarnessGateway.Launcher.Core/Settings/`

- `LauncherSettings` shape unchanged: `Backends` list + `DefaultBackend` name pointer + `SchemaVersion` int [`DECISIONS-LmProxy-agent-launcher.md#T021`], [`DECISIONS-LmProxy-agent-launcher.md#T011`].
- `Backend.ApiKey` becomes a secure-store reference, not plaintext [`DECISIONS-LmProxy-agent-launcher.md#T040`], [`DECISIONS-LmProxy-agent-launcher.md#T018`] (superseded in part).
- New `ISecureKeyStore` abstraction with three implementations: DPAPI (Windows), Keychain (macOS), Secret Service/keyring (Linux) [`DECISIONS-LmProxy-agent-launcher.md#T040`].
- Settings loader migrates older `SchemaVersion` forward on read (first case: plaintext keys → secure store references); refuses newer versions with NEW exit code 9; append-only code table [`DECISIONS-LmProxy-agent-launcher.md#T039`].
- Dangling `DefaultBackend` (hand-edited file) errors and directs the user to `config backend switch` [`DECISIONS-LmProxy-agent-launcher.md#T033`].

## Workstream D — CLI contract surface

### `src/HarnessGateway.Launcher/Commands/`

- Add `-m` as the sole short alias of `--model`; no other short flags [`DECISIONS-LmProxy-agent-launcher.md#T041`].
- Exit codes: 3 label broadened [`DECISIONS-LmProxy-agent-launcher.md#T028`]; code 8 added [`DECISIONS-LmProxy-agent-launcher.md#T031`]; code 9 added [`DECISIONS-LmProxy-agent-launcher.md#T039`]; `Commands/LaunchExitCode.cs` enum + README table updated together (documented-contract rule [`DECISIONS-LmProxy-agent-launcher.md#T012`], AGENTS.md contract note).

## Workstream E — Wizard

### `src/HarnessGateway.Launcher/` (wizard pipeline)

- Preserve D019 step order: harness → endpoint (prefilled from resolved default entry) → key if needed → model (skip-gated per declaration, warned per T027 when undeclared) [`DECISIONS-LmProxy-agent-launcher.md#T034`], [`DECISIONS-LmProxy-agent-launcher.md#D019`], [`DECISIONS-LmProxy-agent-launcher.md#T027`].
- After harness selection, check `SupportedProtocols` against the resolved default backend's protocol; incompatible pairing re-prompts with annotation (no new step) [`DECISIONS-LmProxy-agent-launcher.md#T034`], [`DECISIONS-LmProxy-agent-launcher.md#T028`].
- Wizard stays write-free end-to-end [`DECISIONS-LmProxy-agent-launcher.md#T013`], [`DECISIONS-LmProxy-agent-launcher.md#T016`].

## Blueprint tasks (non-decision chores)

- README: broaden code-3 label; add code-8 and code-9 rows; document `-m`; document `config backend` verbs and `--backend` layering [`DECISIONS-LmProxy-agent-launcher.md#T028`], [`DECISIONS-LmProxy-agent-launcher.md#T031`], [`DECISIONS-LmProxy-agent-launcher.md#T039`], [`DECISIONS-LmProxy-agent-launcher.md#T041`].
- CI builds/verifies per-OS binaries per release (T003 amended to binaries-only; the NuGet tool channel was dropped) [`DECISIONS-LmProxy-agent-launcher.md#T003`].
- Session housekeeping: delete the Decision Ledger and retire the blueprint once implementation completes (persisted-by-default artifacts; TODO.md housekeeping entry).
- Real-harness integration matrix stays deferred this phase — fakes-only testing per [`DECISIONS-LmProxy-agent-launcher.md#T009`], [`DECISIONS-LmProxy-agent-launcher.md#T043`].

## Tests (fakes strategy)

- TUnit, native Microsoft Testing.Platform, temp-dir config writes, stub HTTP handlers, scripted fake harnesses [`DECISIONS-LmProxy-agent-launcher.md#T009`].
- Cover: Codex TOML round-trip + collision refusal, restore-on-exit + self-heal for both new adapters, protocol-compat failures (exit 3), backend resolution + layering, verb CRUD + guards, secure-store abstraction (fake-backed in tests), migration forward + exit 9, exit-8 paths, `-m` parsing.

## Ledger Reference

Cited records: D001, D007, D008, D009, D012, D013, D014, D015, D017, D019, D021, D022; T003, T005, T008, T009, T010, T011, T012, T013, T015, T016, T017, T018, T019, T020, T021, T023, T025, T026, T027, T028, T029, T030, T031, T032, T033, T034, T035, T036, T037, T038, T039, T040, T041, T042, T043, T044 — all in `docs/decisions/DECISIONS-LmProxy-agent-launcher.md`.
