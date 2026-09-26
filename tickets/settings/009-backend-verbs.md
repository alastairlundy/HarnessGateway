---
title: config backend profile verbs
classification: Independent
blocked_by: ["launch/006-backend-resolution.md", "settings/007-secure-key-store.md", "settings/008-settings-migration.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Expose `config backend add|switch|list|remove` as full profile management over the saved backends list and default pointer, with guarded mutation semantics and key input through the secure store.

## What to build

- New `src/HarnessGateway.Launcher/Commands/BackendCommands.cs` (or a sibling file wired under the existing `config` command) with four verbs operating on the settings backends list and `DefaultBackend` pointer; no settings-file format change.
- `add <name> --endpoint --protocol` (plus optional `--key`) - create or replace an entry; the endpoint/protocol pairings follow the launch override rules (raw-URL pairing rule errors otherwise). Keys go only to the secure store via the TK007 abstraction, persisted as a reference in the settings file - never plaintext.

- Key input on add - hidden terminal prompt when no `--key` flag is given (the prompt asks for a key only when the user requests one); `--key <secret>` for scripts, documented helpfully with its shell-history risk. A key is optional at add; keyless backends are valid.
- `switch <name>` - validates the target exists, writes the pointer, prints a confirmation echo; idempotent on already-default; never mutates the entry itself (entry updates are `add`'s job).
- `list` - read-only; show entries with the default marker.
- `remove <name>` - refuses to remove the current default (requires a `switch` first) and refuses to remove the final remaining entry (settings never become backend-less).
- Unknown names on switch and remove exit code 8 with the unknown-backend-name message (the contract from TK006); best-effort close-match hints stay non-contractual.
- Non-interactive write scope only - these verbs write settings; the wizard never does. Keep new subcommands lean - the plan for the unified CLI is to structure the command surface later; avoid deep nesting.
- README documentation for the verbs and the add key-input contract.

## Size

- Files - 5 (new command file, root wiring, tests; README already mostly up to date from TK006/TK008 edits)

## Recommended Workflow

### Step 1 - Scaffold the command group

Where: src/HarnessGateway.Launcher/Commands/BackendCommands.cs (new), wiring in root or under ConfigCommand.cs

- Implement the filename/project-placement convention per repo style.

Verify: command parses and shows usage.

### Step 2 - Implement switch and list

Where: BackendCommands.cs

- Validate-and-point semantics for switch (idempotent, confirmation echo); read-only list with a default marker.

Verify: tests - default pointer set; idempotent success; list output shape.

### Step 3 - Implement add (including key input) and the guards

Where: BackendCommands.cs

- Create-or-replace semantics; endpoint/protocol pairing validation reusing the Core resolver's rules; key from `--key` or a hidden prompt routed to the secure-store abstraction from TK007; unknown names exit 8; remove guards for default-pointer and last-remaining-entry.

Verify: tests - create, replace, guards refusing the last entry and the default, unknown exit 8, key stored reference only (fixture JSON contains no secret).

### Step 4 - Document the verbs

Where: README.md

- Verbs section covering add/switch/list/remove surface plus the `--key` shell-history trade-off.

Verify: README rows aligned with behaviour.

## Context pointers

##### Files

- src/HarnessGateway.Launcher/Commands/ConfigCommand.cs - the existing config group and its default-backend resolution
- src/HarnessGateway.Launcher/Program.cs - wiring the new verbs
- src/HarnessGateway.Launcher.Core/Settings/SettingsStore.cs - load path (migration already adopted by TK008's loader)
- src/HarnessGateway.Launcher.Core/Backends/BackendResolution.cs - the Core resolver TK006 introduces

##### ADRs

- Decision ledger only.

##### Domain terms

- Backend, Adapter.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T030 - the verbs inventory and non-interactive write scope
- DECISIONS-LmProxy-agent-launcher.md#T032, T033, T035 - switch semantics, remove guards, key input
- DECISIONS-LmProxy-agent-launcher.md#T031, T038, T040, T016, D017, D021, T011 - failure class reuse, hint policy, store-only keys, write scope, lean commands, protocol word, schema shape

## Acceptance criteria

- [ ] `config backend add|switch|list|remove` exist and operate with documented guard semantics for default and last-remaining entry
- [ ] Switch prints the confirmation echo, is idempotent, and never mutates an entry or unknown name exits 8 per the TK006-established contract
- [ ] Add handles create-or-replace with the endpoint/protocol pairing rule; key input via hidden prompt or `--key` writes only a reference into JSON (store-only secret)
- [ ] List is read-only and shows the default marker; `dotnet test --solution src/HarnessGateway.slnx` covers verb CRUD (create/replace/list semantics) and guards

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - launch/006-backend-resolution.md (resolver reuse and exit-8 contract), settings/007-secure-key-store.md (key routing), settings/008-settings-migration.md (loader that all read paths adopt).
