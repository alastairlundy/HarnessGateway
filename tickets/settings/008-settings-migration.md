---
title: Settings migration on SchemaVersion
classification: Independent
blocked_by: ["settings/007-secure-key-store.md", "launch/006-backend-resolution.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Make settings load forward-migrate legacy files on read and refuse newer schema versions, with exit code 9 as the new append-only contract row.

## What to build

- Loader migration in `src/HarnessGateway.Launcher.Core/Settings/SettingsStore.cs` - on read, if a file's `SchemaVersion` is older than or equal to the tool's own version, migrate it forward automatically and write the migrated file back; if newer, fail with exit code 9 ("unsupported settings schema version").
- Plain-text ApiKey values are not discriminated by shape with complete reliability, so the version field is the migration trigger. Implement the bumped version as the tool's post-migration version with the migration cases keyed on the older versions. Flagged interpretation - a stored file migrated by this tool cannot be read by a pre-migration binary; that forward-only consequence is accepted per record T039's record.
- Migration runs before any read-path behaviour (launch and config verbs), is forward-only and irreversible per file. The first concrete migration case is plaintext keys becoming store references (TK007 store).
- Add exit code 9 to `src/HarnessGateway.Launcher/Commands/LaunchExitCode.cs` and append its README row; no existing code's value, ordering or label changes (append-only contract rule); do not stretch code 7 to cover schema refusal.
- Persist family and dangling-default handling don't belong here - the launch read policy lives in TK006 and I037 already covers its exit code.

## Size

- Files - 5 (loader, exit enum, README, tests)

## Recommended Workflow

### Step 1 - Migration plumbing at load time

Where: src/HarnessGateway.Launcher.Core/Settings/SettingsStore.cs

- Read the version field; dispatch to migration per older version; write the migrated file with the bumped version before any consumer reads the loaded state.

Verify: a version-1 fixture read produces a version-bumped, store-referenced file on disk.

### Step 2 - Refuse newer versions

Where: SettingsStore.cs (typed failure) and src/HarnessGateway.Launcher/Commands/LaunchExitCode.cs

- A newer version surfaces as a typed migration-refusal that the CLI maps to exit 9 with an instructive message.

Verify: mapping test - newer version file yields exit 9 on launch and config read paths.

### Step 3 - README contract row

Where: README.md - append code 9.

Verify: row appended; no existing row changed.

## Context pointers

##### Files

- src/HarnessGateway.Launcher.Core/Settings/SettingsStore.cs - load path
- src/HarnessGateway.Launcher.Core/Settings/LauncherSettings.cs - version field
- src/HarnessGateway.Launcher/Commands/LaunchExitCode.cs - contract enum
- README.md - contract rows

##### ADRs

- Decision ledger only.

##### Domain terms

- Backend - field that migrates.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T039 - the migration record (forward-only, refuse newer, code 9, append-only)
- DECISIONS-LmProxy-agent-launcher.md#T021, T011 - the schema being versioned
- DECISIONS-LmProxy-agent-launcher.md#T040 - the first migration case direction (plaintext keys to store references)
- DECISIONS-LmProxy-agent-launcher.md#T012 - append-only exit-code amendment rule
- DECISIONS-LmProxy-agent-launcher.md#D012, T003, T018 - the secret-splitting context

## Acceptance criteria

- [ ] Version-1 plaintext-key file migrates on read; result is written with the bumped version before any read-path behaviour
- [ ] Refusing a newer version yields exit code 9 and no partial state (code 7 untouched)
- [ ] Migration runs ahead of both launch and config read paths
- [ ] LaunchExitCode enum and README row updated together; forward-only rule documented

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - settings/007-secure-key-store.md (the store that receives migrated keys), launch/006-backend-resolution.md (exit-code file and README same-file ordering).
