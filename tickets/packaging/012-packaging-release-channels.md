---
title: Packaging and release channels
classification: Independent
blocked_by: [001-solution-and-project-scaffolding, 009-direct-launch-path, 010-config-command, 011-launch-wizard]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Ship each release as both a NuGet dotnet tool package and per-OS GitHub Release binaries, under the `lmlaunch` working name, with the exit-code contract documented across channels.

## What to build

- Packaging metadata on the app project (dotnet tool pack configuration; tool id `lmlaunch` until the ledger's rename decision).
- A release pipeline producing per-OS binaries (Windows, Linux, macOS) and the NuGet tool package - both artifact kinds built and verified per release.
- Documentation of the exit-code table and per-OS install across the release channels (README section).
- Tests are not the deliverable here; verification is that both artifact kinds build and the tool runs.

## Size

- **Files** - 4 (edit the app csproj, create the release workflow, edit the README, optional publish script)

## Recommended Workflow

### Step 1 - Add packaging metadata

Where: `src/LmProxy.AgentLauncher/LmProxy.AgentLauncher.csproj`

- Configure dotnet tool packing with tool id `lmlaunch`.

Verify: `dotnet pack` produces a tool package whose id is `lmlaunch`.

### Step 2 - Add the release pipeline

Where: `.github/workflows/` (new release workflow)

- Build and publish per-OS binaries plus the NuGet tool package as release artifacts.

Verify: a dry-run publish succeeds for each target operating system.

### Step 3 - Document exit codes and install

Where: `README.md`

- Add the exit-code table and per-OS install instructions for both channels.

Verify: the documented table matches the implemented contract.

### Step 4 - Verify both artifact kinds

Where: N/A

- Run the release pipeline end to end.

Verify: binaries and the tool package are present as artifacts.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher/LmProxy.AgentLauncher.csproj` - packaging metadata
- `README.md` - user-facing documentation
- `src/LmProxy.AgentLauncher/Commands/LaunchCommand.cs` - source of the documented exit-code table

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

None - packaging is domain-neutral.

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D017` - working root name `lmlaunch` until the rename
- `DECISIONS-LmProxy-agent-launcher.md#T003` - both channels; two artifact kinds built and verified
- `DECISIONS-LmProxy-agent-launcher.md#T012` - exit-code table documented across release channels

## Acceptance criteria

- [ ] `dotnet pack` produces a dotnet tool package with id `lmlaunch`
- [ ] The release pipeline produces per-OS binaries and the NuGet tool package, both verified
- [ ] The exit-code table and per-OS install are documented in the README
- [ ] Tool id and binary names use the working root name

## Dependencies

**Blocked by** - 001-solution-and-project-scaffolding, 009-direct-launch-path, 010-config-command, 011-launch-wizard
