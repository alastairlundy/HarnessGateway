---
title: Codex adapter
classification: Independent
blocked_by: ["adapters/001-harness-metadata-supported-protocols.md", "adapters/002-toml-roundtrip-editor.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Add the Codex harness as a fully supported launcher target: plan a backed-up, launcher-owned TOML provider block plus top-level keys, inject the saved API key under the `env_key`-named environment variable, and launch without one when no key is saved.

## What to build

- New `src/HarnessGateway.Launcher.Core/Adapters/CodexAdapter.cs` implementing the existing adapter interface exactly like the four current adapters (metadata, detection, planning; no execution).
- Metadata - name `codex`, kind Cli, requires model, supports only the openai-responses protocol (from TK001's field), detection probing `codex` on PATH, install hint covering npm/brew, config file candidates resolving the real config path - honouring a user-set `CODEX_HOME` environment variable, falling back to the user's `~/.codex/config.toml`.
- `PlanLaunchAsync` emits exactly two actions - one `ConfigFileWriteAction` writing a `[model_providers.harnessgateway]` block (`name`, `base_url`, `env_key`, `wire_api = "responses"`) plus top-level `model_provider` and `model` keys, and one `EnvironmentVariableAction` carrying the saved key under the `env_key` variable's name. The written TOML is produced with the round-trip editor from TK002.
- Pre-write collision check per T037 - if the user's config already contains a block with the id `harnessgateway`, fail before any write with an explanatory error; do not adopt or overwrite it.
- Key handling - inject the saved key only when present; launch without one when absent; never a dummy value.
- Restore-on-exit and self-heal machinery is Core-owned already; the adapter needs no lifecycle work, only `--persist config` ordering must keep the writes.
- Register the adapter in the adapter list in `src/HarnessGateway.Launcher/Program.cs`.

## Size

- Files - 5 (new adapter, Program.cs registration line, new test files)

## Recommended Workflow

### Step 1 - Write the adapter metadata and detection

Where: src/HarnessGateway.Launcher.Core/Adapters/CodexAdapter.cs

- Follow the shape of the existing adapters, including the internal detect-command delegate for test seams.

Verify: metadata values match the record set exactly.

### Step 2 - Plan the config write and key action

Where: src/HarnessGateway.Launcher.Core/Adapters/CodexAdapter.cs

- Resolve the real config path honouring `CODEX_HOME`; splice the provider block and top-level keys with the round-trip editor; refuse on block collision before planning the write.

Verify: splice output is a diff of exactly the added block plus two top-level keys.

### Step 3 - Cover the contract with tests

Where: src/HarnessGateway.Launcher.Tests/Adapters/CodexAdapterTests.cs (new)

- Round-trip output shape, collision refusal (no write), key present/absent behaviour (no dummy), `CODEX_HOME` resolution, restore-on-exit and self-heal via the existing fake backups lane.

Verify: `dotnet test --solution src/HarnessGateway.slnx` covers collision and restore cases.

### Step 4 - Register and smoke through orchestrator tests

Where: src/HarnessGateway.Launcher/Program.cs

- Append the adapter to the adapter list; extend orchestrator tests to drive a codex launch against a fake.

Verify: direct launch plan executes with a fake; restore keeps the file unless persist is set.

## Context pointers

##### Files

- src/HarnessGateway.Launcher.Core/Adapters/IHarnessAdapter.cs - the interface
- src/HarnessGateway.Launcher.Core/Configuration/BackupService.cs - restore/self-heal machinery
- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - plan-executor that consumes the actions
- src/HarnessGateway.Launcher/Program.cs - registration site

##### ADRs

- Decision ledger only.

##### Domain terms

- Adapter, Harness, Configuration action, Backend - GLOSSARY.md definitions.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T025 - adapter spec and constraints
- DECISIONS-LmProxy-agent-launcher.md#T037 - fixed provider id `harnessgateway` and collision refusal
- DECISIONS-LmProxy-agent-launcher.md#T008, T019, T020, T023, T005 - plan/execute pattern and metadata
- DECISIONS-LmProxy-agent-launcher.md#D011, D015, D007, D012 - no lasting damage, foreground launch, deferred-now-added, key handling

## Acceptance criteria

- [ ] Metadata carries name, kind as Cli, requirement and install hint exactly per record values
- [ ] Provider-block collision refuses launch before any write
- [ ] Key-injecting env action only when the backend has a saved key; never a dummy value
- [ ] `CODEX_HOME`-aware config path resolution has a passing test for a custom home directory
- [ ] Restore-on-exit and self-heal tests pass for the Codex write; `--persist config` keeps the write

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - adapters/001-harness-metadata-supported-protocols.md, adapters/002-toml-roundtrip-editor.md.
