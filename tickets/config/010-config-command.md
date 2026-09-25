---
title: Config command
classification: Independent
blocked_by: [002-settings-domain-and-store, 009-direct-launch-path]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Give saved defaults a home - `config` verbs that show and set the default endpoint and protocol, set or clear the saved key, and set the default model. These are the primary non-interactive settings writers.

## What to build

- `Commands/ConfigCommand.cs` with verbs: show (default endpoint + protocol, saved-key presence, default model), set default endpoint (+ protocol), set/clear saved key, set default model.
- These verbs - plus explicit `--persist model` on direct launches - are the only settings writers in the program. They edit the default entry of the backends list.
- The saved key is stored as plaintext in the settings JSON in version 1 (hardening deferred).
- Register the `config` subcommand into the root command created by the direct-launch-path ticket.
- Tests: verb behavior against temp settings files.

## Size

- **Files** - 3 (1 source, 2 test)

## Recommended Workflow

### Step 1 - Config command skeleton and show verb

Where: `src/LmProxy.AgentLauncher/Commands/ConfigCommand.cs`

- Create the `config` subcommand with the show verb rendering the current default entry.

Verify: show renders the default endpoint, protocol, key presence, and default model.

### Step 2 - Set default endpoint and protocol

Where: `src/LmProxy.AgentLauncher/Commands/ConfigCommand.cs`

- Implement set-default-endpoint with protocol validation against the three values.

Verify: the settings file updates; an invalid protocol is rejected as usage (exit 2).

### Step 3 - Saved key and default model verbs

Where: `src/LmProxy.AgentLauncher/Commands/ConfigCommand.cs`

- Implement set/clear saved key and set default model against the default backend entry.

Verify: round-trip tests against a temp settings file.

### Step 4 - Register into the root command

Where: `src/LmProxy.AgentLauncher/Program.cs`

- Add the `config` subcommand to the `lmlaunch` root command.

Verify: `lmlaunch config --help` lists the verbs.

### Step 5 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher/Commands/ConfigCommand.cs` - new file
- `src/LmProxy.AgentLauncher/Program.cs` - root command registration
- `src/LmProxy.AgentLauncher.Core/Settings/SettingsStore.cs` - the store these verbs read and write

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Default endpoint - the user-saved backend these verbs show and edit
- Backend - the default entry of the backends list

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D009` - the config command closes the saved-default loop
- `DECISIONS-LmProxy-agent-launcher.md#D012` - saved key plaintext in version 1
- `DECISIONS-LmProxy-agent-launcher.md#D016` - config subcommand in scope
- `DECISIONS-LmProxy-agent-launcher.md#T006` - settings edited via config subcommands
- `DECISIONS-LmProxy-agent-launcher.md#T011` - config verbs edit the default entry
- `DECISIONS-LmProxy-agent-launcher.md#T014` - per-backend default model settable only via config
- `DECISIONS-LmProxy-agent-launcher.md#T016` - settings-writer scope

## Acceptance criteria

- [ ] `config show` renders the default endpoint, protocol, saved-key presence, and default model
- [ ] Set-default-endpoint validates the protocol against the three values and persists to the default entry
- [ ] Set/clear saved key and set default model persist correctly
- [ ] The `config` subcommand is registered under the `lmlaunch` root command
- [ ] Verb tests against temp settings files pass

## Dependencies

**Blocked by** - 002-settings-domain-and-store, 009-direct-launch-path
