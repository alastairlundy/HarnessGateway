---
title: Backup, restore, and self-heal service
classification: Independent
blocked_by: [004-adapter-and-launch-contracts]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Make configuration writes safe by default - atomic backups before the first write, restore on harness exit, and self-heal of leftover backups at the start of the next launch - so the launcher never causes lasting damage to the user's setup.

## What to build

- `Configuration/BackupService.cs`:
  - Before the first config-file write in a launch, back up each target - original path, original bytes, and a persisted marker - in one atomic file operation.
  - On harness exit, restore the originals unless `--persist config` was given for that launch; the launcher never causes lasting damage by default.
  - At the start of every launch, self-heal: restore leftover backups whose marker is NOT persisted; deliberately persisted backups are skipped.
  - Expose Ctrl+C/TERM handler registration so the launch path can restore before exiting on interrupt.
- Backup files must carry enough context (target path and original bytes) to restore unambiguously.
- Tests: a fake harness runs and exits - originals restored; a kill simulation leaves a marked backup that self-heal does NOT undo when persisted; a leftover unmarked backup is restored at the next launch before new configuration actions.

## Size

- **Files** - 3 (1 source, 2 test)

## Recommended Workflow

### Step 1 - Design the backup record layout

Where: `src/LmProxy.AgentLauncher.Core/Configuration/BackupService.cs`

- Define the on-disk backup shape: target path, original bytes, persisted marker.

Verify: a unit test writes and reads back a backup.

### Step 2 - Implement atomic backup-before-first-write

Where: `src/LmProxy.AgentLauncher.Core/Configuration/BackupService.cs`

- Back up each target exactly once per launch, in one atomic file operation, before the first write to it.

Verify: a test asserts originals are captured exactly once across multiple writes to the same target.

### Step 3 - Implement restore-on-exit plus signal handlers

Where: `src/LmProxy.AgentLauncher.Core/Configuration/BackupService.cs`

- Restore originals on normal exit unless the launch was marked persisted; register Ctrl+C/TERM handlers that restore before exiting.

Verify: a simulated normal exit restores; a simulated interrupt restores via the handler.

### Step 4 - Implement the self-heal scan

Where: `src/LmProxy.AgentLauncher.Core/Configuration/BackupService.cs`

- At launch start, restore leftover backups whose marker is not persisted; skip deliberately persisted backups.

Verify: an unmarked leftover is restored; a persisted leftover is skipped.

### Step 5 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher.Core/Configuration/` - target folder
- `src/LmProxy.AgentLauncher.Core/Configuration/ConfigurationAction.cs` - ConfigFileWriteAction targets are what get backed up

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Configuration action - a step that makes a harness use a chosen backend
- Harness - the process whose exit triggers restore

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D010` - the backup enables recovery from abnormal exits
- `DECISIONS-LmProxy-agent-launcher.md#D011` - restore-on-exit by default; explicit persist opt-in
- `DECISIONS-LmProxy-agent-launcher.md#T008` - try/finally plus Ctrl+C/TERM handlers plus leftover-backup self-heal
- `DECISIONS-LmProxy-agent-launcher.md#T015` - persisted-marker carve-out; `--persist config` suppresses restore for that launch

## Acceptance criteria

- [ ] Each target is backed up (path + original bytes + persisted marker) in one atomic operation before its first write in a launch
- [ ] Originals are restored on harness exit unless `--persist config` was given for that launch
- [ ] Self-heal at launch start restores leftover unmarked backups and skips deliberately persisted ones
- [ ] Ctrl+C/TERM handlers restore before exiting
- [ ] The restore, kill-simulation, and self-heal tests pass

## Dependencies

**Blocked by** - 004-adapter-and-launch-contracts
