---
title: Direct launch path
classification: Independent
blocked_by: [002-settings-domain-and-store, 005-backup-restore-self-heal, 006-launch-runner, 007-opencode-kilo-adapters, 008-copilot-pi-adapters]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Build the non-interactive launch path - `lmlaunch launch <agent>` with its flag surface, exit-code contract, and the full orchestrator pipeline from settings through restore.

## What to build

- `Program.cs` - root command named `lmlaunch` (working name until the ledger's rename decision) exposing `launch` and `config` subcommands; the `config` verb is registered by the config-command ticket, the no-args wizard branch by the wizard ticket.
- `Commands/LaunchCommand.cs` - `launch <agent>` direct mode with long flags only (no short aliases in version 1): `--endpoint <url>`, `--protocol <openai-chat|openai-responses|anthropic>`, `--model <id>`, `--persist <config|model>` (repeatable, comma-list sugar accepted). Raw-URL overrides require both `--endpoint` and `--protocol`; no protocol probing on the launch path. `--persist model` saves the chosen model as the per-backend default model in settings; `--persist config` suppresses restore-on-exit for that launch's file writes.
- `Commands/LaunchOrchestrator.cs` - pipeline order: load settings - resolve backend (saved default at zero flag cost, or explicit override) - self-heal leftover unmarked backups - availability check (direct mode fails with the adapter's install hint before any configuration action) - adapter plans - Core executes configuration actions with backup - foreground run to exit - restore unless persisted.
- Exit codes per class: 0 ok; 2 usage; 3 unknown agent; 4 harness unavailable; 5 endpoint unreachable; 6 required model missing; 7 config/restore failure. A declared-required model absent in direct mode produces a warning and the launch proceeds (warn-and-launch); exit code 6 remains part of the documented contract for the missing-required-model failure class.
- Settings writes originate only from `config` subcommands or explicit `--persist model` on this non-interactive path - never from the wizard.
- Tests: flag parsing and validation; exit-code mapping; orchestrator pipeline end-to-end with fake adapters, a fake runner, and temp directories.

## Size

- **Files** - 5 (edit `Program.cs`, create `Commands/LaunchCommand.cs`, create `Commands/LaunchOrchestrator.cs`, 2 test files)
- **Large Edits required**

## Recommended Workflow

### Step 1 - Root command and launch subcommand skeleton

Where: `src/LmProxy.AgentLauncher/Program.cs`, `src/LmProxy.AgentLauncher/Commands/LaunchCommand.cs`

- Create the `lmlaunch` root command and the `launch` subcommand with flag parsing and validation (endpoint requires protocol; persist values config or model; repeatable plus comma-list sugar).

Verify: parsing tests cover the endpoint+protocol pairing, persist values, and sugar forms.

### Step 2 - Exit-code mapping

Where: `src/LmProxy.AgentLauncher/Commands/`

- Map each failure class to its documented code (0, 2, 3, 4, 5, 6, 7).

Verify: mapping tests for every failure class.

### Step 3 - Orchestrator pipeline

Where: `src/LmProxy.AgentLauncher/Commands/LaunchOrchestrator.cs`

- Wire settings load - backend resolve - self-heal - availability check - plan - execute with backup - foreground run - restore unless persisted.

Verify: an end-to-end fake-adapter test in a temp directory walks the full pipeline.

### Step 4 - Persist semantics

Where: `src/LmProxy.AgentLauncher/Commands/`

- `--persist model` writes the per-backend default model to settings; `--persist config` suppresses restore for that launch.

Verify: tests assert the settings write and the leftover persisted backup.

### Step 5 - Failure classes

Where: `src/LmProxy.AgentLauncher/Commands/LaunchCommand.cs`

- Unknown agent exits 3; unavailable harness exits 4 with the install hint before any configuration action.

Verify: tests for both classes.

### Step 6 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher/Program.cs` - root command home
- `src/LmProxy.AgentLauncher.Core/Settings/SettingsStore.cs` - settings load and the `--persist model` write
- `src/LmProxy.AgentLauncher.Core/Configuration/BackupService.cs` - self-heal entry point and restore
- `src/LmProxy.AgentLauncher.Core/Execution/LaunchRunner.cs` - foreground execution
- `src/LmProxy.AgentLauncher.Core/Adapters/` - the four adapters being orchestrated

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Backend - the resolved endpoint for this launch
- Default endpoint - the saved backend used at zero flag cost
- Adapter - the planner providing the LaunchPlan
- Harness - the process being launched
- Configuration action - what Core executes with backup

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D001` - session goal: configure and start harnesses in the current working directory
- `DECISIONS-LmProxy-agent-launcher.md#D004` - direct entrypoint acts on the current working directory
- `DECISIONS-LmProxy-agent-launcher.md#D009` - saved default at zero flag cost; per-launch override
- `DECISIONS-LmProxy-agent-launcher.md#D011` - restore unless persisted
- `DECISIONS-LmProxy-agent-launcher.md#D013` - direct mode accepts `--model`
- `DECISIONS-LmProxy-agent-launcher.md#D014` - warn on missing required model in direct mode
- `DECISIONS-LmProxy-agent-launcher.md#D015` - foreground run; launcher exits with the harness exit code
- `DECISIONS-LmProxy-agent-launcher.md#D016` - named root command with launch and config subcommands
- `DECISIONS-LmProxy-agent-launcher.md#D017` - working root name `lmlaunch`
- `DECISIONS-LmProxy-agent-launcher.md#D018` - fail with install hint before any configuration action
- `DECISIONS-LmProxy-agent-launcher.md#D021` - `--protocol` values
- `DECISIONS-LmProxy-agent-launcher.md#T008` - self-heal runs at the start of every launch
- `DECISIONS-LmProxy-agent-launcher.md#T010` - flag naming; probe-free overrides
- `DECISIONS-LmProxy-agent-launcher.md#T011` - default backend resolution
- `DECISIONS-LmProxy-agent-launcher.md#T012` - exit-code contract
- `DECISIONS-LmProxy-agent-launcher.md#T015` - persist semantics; repeatable plus comma-list sugar
- `DECISIONS-LmProxy-agent-launcher.md#T016` - settings-writer scope

## Acceptance criteria

- [ ] `lmlaunch launch <agent>` parses `--endpoint`, `--protocol`, `--model`, and repeatable `--persist` with comma-list sugar; raw-URL overrides require both endpoint and protocol; no protocol probing
- [ ] The orchestrator pipeline runs in the documented order, including self-heal before new configuration actions and restore unless persisted
- [ ] Direct mode fails with the adapter's install hint (exit 4) before any configuration action when the harness is unavailable
- [ ] Exit codes match the documented table; a missing declared-required model warns and proceeds
- [ ] `--persist model` writes the per-backend default model; `--persist config` leaves that launch's writes with a persisted marker
- [ ] Parsing, mapping, and pipeline tests pass

## Dependencies

**Blocked by** - 002-settings-domain-and-store, 005-backup-restore-self-heal, 006-launch-runner, 007-opencode-kilo-adapters, 008-copilot-pi-adapters
