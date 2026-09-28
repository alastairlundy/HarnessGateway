---
title: Codex adapter
classification: Independent
blocked_by: ["adapters/001-harness-metadata-supported-protocols.md", "adapters/002-toml-roundtrip-editor.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Add Codex as a launcher target: plan one backed-up, launcher-owned provider block plus top-level model keys in `config.toml`, inject the saved API key under the `env_key`-named variable, and launch in the working directory. The adapter only plans - Core executes, backs up, restores, and self-heals per the existing machinery.

## What to build

- New adapter at `src/HarnessGateway.Launcher.Core/Adapters/CodexAdapter.cs`, implementing `IHarnessAdapter` in the established pattern (metadata, detection, `PlanLaunchAsync` - no execution and no new lifecycle code).
- Metadata exactly per record T025/T044
  - Name `codex`, Kind `Cli`, `RequiresModel = true`, supports only the openai-responses protocol (via the TK001 `SupportedProtocols` field), install hint covering npm/brew, `DetectionCommand` probing `codex` on PATH (internal test seam like the four existing adapters).
  - `ConfigFileCandidates` resolve the real config path honouring a user-set `CODEX_HOME` environment variable, falling back to the user profile's `~/.codex/config.toml`.
- `PlanLaunchAsync` emits exactly one `ConfigFileWriteAction`, nothing more
  1. `ConfigFileWriteAction` for the TOML edit - splice a `[model_providers.harnessgateway]` block (fields `name`, `base_url`, `env_key`, `wire_api = "responses"`) plus top-level `model_provider = "harnessgateway"` and `model` keys, using the round-trip editor from TK002 so comments and structure survive. The model key is written only when a model was chosen.
- Key delivery (record D012, delivery per T045) - the saved key rides on the plan's `ProcessConfiguration.EnvironmentVariables` under the variable named by `env_key`, scoped to the spawned codex process; only when the resolved backend entry has one; never a dummy value.
- Collision refusal before any write (record T037) - if the current `config.toml` already defines a provider with the id `harnessgateway`, planning fails with an explanatory error and no action is returned. Never adopt, overwrite, or shadow another provider id; probe via the TK002 editor's name-in-use check (headers, table arrays, inline tables, and dotted keys).
- Launch and configuration footprint stays inside the closed action family (record T023 as amended by T045) - no new action variant is introduced.
- Register the adapter in the `List<IHarnessAdapter>` in `src/HarnessGateway.Launcher/Program.cs` (append after `PiAdapter()`), matching the agent-lookup convention: launch resolves `codex` case-insensitively against `Metadata.Name`.

## Size

- Files - 6 (new adapter, `Program.cs` registration, adapter tests, orchestrator-side codex test additions)
- Large Files to be created - not expected (adapter should stay well under 500 lines, matching neighbouring adapters)

## Recommended Workflow

### Step 1 - Write the adapter shell

Where: src/HarnessGateway.Launcher.Core/Adapters/CodexAdapter.cs (new)

- Model the file on `OpenCodeAdapter.cs`/`KiloAdapter.cs`: metadata initialiser, `IsAvailableAsync` probing `codex`, internal `detectCommand` delegate constructor seam for tests.
- Add a `CODEX_HOME`-aware config-path resolver (read the environment variable; fall back to the user profile path).

Verify: metadata values match the record values exactly; path resolver unit spot-checks read right with a set `CODEX_HOME`.

### Step 2 - Plan the action and the process env

Where: same file

- Call the TK002 editor's name-in-use check on the resolved config path's content; refuse if `harnessgateway` is claimed by any header, table array, inline table, or dotted key.
- Splice block and top-level keys; emit the `ConfigFileWriteAction` with the round-tripped TOML string.
- Set the env_key-named variable on the plan's `ProcessConfiguration.EnvironmentVariables` only when the backend entry's saved key resolves.

Verify: splice is a minimal diff (one block plus two top-level keys); refusal path returns an error before returning any action; absent key yields no second action.

### Step 3 - Cover the contract with tests

Where: src/HarnessGateway.Launcher.Tests/Adapters/CodexAdapterTests.cs (new)

- Round-trip output shape and comment preservation.
- Collision refusal asserts no write (and a failure, not an overwrite).
- Key present/absent behaviour (present injects the corresponding variable name and value; absent omits the action entirely - no dummy).
- `CODEX_HOME` resolution to a custom directory.
- Restore-on-exit and self-heal via the existing fake backups lane in orchestrator tests; `--persist config` keeps the write.

Verify: `dotnet test --solution src/HarnessGateway.slnx` covers collision refusal and restore paths.

### Step 4 - Register and smoke through the orchestrator

Where: src/HarnessGateway.Launcher/Program.cs, src/HarnessGateway.Launcher.Tests/Commands/LaunchOrchestratorTests.cs

- Append the adapter to the registration list; drive a codex-named direct launch against the fake detection delegate and a temp-dir config file.

Verify: plan executes; the config file is restored after the run unless `config` is in the persist list.

## Context pointers

##### Files

- src/HarnessGateway.Launcher.Core/Adapters/IHarnessAdapter.cs - the interface this adapter implements
- src/HarnessGateway.Launcher.Core/Adapters/OpenCodeAdapter.cs - the closest neighbour in shape
- src/HarnessGateway.Launcher.Core/Configuration/BackupService.cs - backup/restore/self-heal machinery the adapter rides on
- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - the plan executor that consumes the emitted actions
- src/HarnessGateway.Launcher/Program.cs - registration site
- tickets/adapters/002-toml-roundtrip-editor.md - the editor surface this adapter consumes

##### ADRs

- Decision ledger only; `docs/adr/` does not exist in this repository.

##### Domain terms

- Adapter, Harness, Configuration action, Backend (all defined in GLOSSARY.md).

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T025 - the Codex adapter spec (metadata, plan shape, key handling, `CODEX_HOME`, round-trip, restore)
- DECISIONS-LmProxy-agent-launcher.md#T037 - fixed provider id `harnessgateway`, collision refusal semantics
- DECISIONS-LmProxy-agent-launcher.md#T005, T019, T020, T023 - locked adapter architecture, metadata record, plan/execute split, closed action family
- DECISIONS-LmProxy-agent-launcher.md#T008, T015 - restore/self-heal unchanged; `--persist config` ordering
- DECISIONS-LmProxy-agent-launcher.md#D011, D015, D012, D007 - no lasting damage, foreground launch, keys, adapter now added
- DECISIONS-LmProxy-agent-launcher.md#T044 - the supported-protocol list the metadata cites
- DECISIONS-LmProxy-agent-launcher.md#T009 - the fakes strategy test drivers

## Acceptance criteria

- [ ] Metadata values match record T025 exactly (name, kind, requirement, protocols, install hint, detection command, config candidates honouring `CODEX_HOME`)
- [ ] `PlanLaunchAsync` emits exactly one `ConfigFileWriteAction`; no other action types
- [ ] A pre-existing `harnessgateway` provider definition refuses the launch before any write, with an explanatory error
- [ ] The written TOML splices cleanly and preserves comments and structure; top-level `model_provider` and `model` keys are set
- [ ] Key handling - the env_key-named variable is set on the plan's process config only with a saved key; absent (and no dummy value) when none
- [ ] Restore-on-exit and self-heal tests pass over the Codex write, and `--persist config` keeps the write
- [ ] Adapter is registered and reachable through `launch codex` (case-insensitive) in orchestrator tests

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - adapters/001-harness-metadata-supported-protocols.md (SupportedProtocols field needed for the metadata), adapters/002-toml-roundtrip-editor.md (round-trip TOML editing).
