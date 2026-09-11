---
title: Adapter and launch contracts
classification: Independent
blocked_by: [002-settings-domain-and-store]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Define the typed seam every adapter plans against and Core executes - harness metadata, the adapter interface, the closed configuration-action family, and the launch request/plan handoff.

## What to build

- `Adapters/HarnessKind.cs` - enum `Cli`, `Gui`. Harnesses may be CLI or GUI apps; detection and launch mechanics must cover both.
- `Adapters/HarnessMetadata.cs` - record `(string Name, HarnessKind Kind, bool RequiresModel, string InstallHint, string? DetectionCommand, IReadOnlyList<string> ConfigFileCandidates)`. A null DetectionCommand means probe the launch command on PATH. ConfigFileCandidates are adapter-resolved (project-local or global).
- `Adapters/IHarnessAdapter.cs` - `Metadata` property, `IsAvailableAsync(CancellationToken)`, `PlanLaunchAsync(LaunchRequest, CancellationToken)` returning a LaunchPlan. Adapters are pure planners; Core executes writes, restores backups, and starts the process.
- `Configuration/ConfigurationAction.cs` - abstract `ConfigurationAction` with exactly three variants: `CliArgumentAction(string Argument, string? Value)`, `ConfigFileWriteAction(string TargetPath, string Content)`, `EnvironmentVariableAction(string Name, string Value)`. Closed family - quirks that cannot fit these three variants re-open it.
- `Launch/LaunchRequest.cs` - record `(Backend Backend, string? Model, string WorkingDirectory)`.
- `Launch/LaunchPlan.cs` - record `(CliInvoke ProcessConfiguration Process, IReadOnlyList<ConfigurationAction> ConfigurationActions)`. Coupled to CliInvoke 3.0.0-beta.3's API shape; beta churn re-opens this contract.
- Keep the seam open for the deferred harness set (Codex, Claude Code, OpenClaw, Hermes, Cline) - new adapters must be addable without interface changes.

## Size

- **Files** - 6

## Recommended Workflow

### Step 1 - Define HarnessKind and HarnessMetadata

Where: `src/LmProxy.AgentLauncher.Core/Adapters/`

- Create the kind enum and the metadata record with the documented shape.

Verify: the project compiles.

### Step 2 - Define the ConfigurationAction family

Where: `src/LmProxy.AgentLauncher.Core/Configuration/ConfigurationAction.cs`

- Create the abstract base and exactly the three variants.

Verify: the project compiles; no fourth variant exists.

### Step 3 - Define LaunchRequest and LaunchPlan

Where: `src/LmProxy.AgentLauncher.Core/Launch/`

- Create both records; LaunchPlan.Process is CliInvoke's ProcessConfiguration.

Verify: a construction smoke check compiles against the CliInvoke 3.0.0-beta.3 API.

### Step 4 - Define IHarnessAdapter

Where: `src/LmProxy.AgentLauncher.Core/Adapters/IHarnessAdapter.cs`

- Create the interface with Metadata, IsAvailableAsync, and PlanLaunchAsync.

Verify: a throwaway fake adapter implements all three members.

### Step 5 - Build the solution

Where: N/A

Verify: `dotnet build src/LmProxy.slnx` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher.Core/Adapters/`, `Configuration/`, `Launch/` - target folders
- `src/LmProxy.AgentLauncher.Core/Backends/Backend.cs` - LaunchRequest carries a Backend

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Adapter - the per-harness component that detects, configures, and starts one harness
- Harness - an agent tool the launcher can configure and start (CLI or GUI app)
- Configuration action - a step that makes a harness use a chosen backend
- Backend - the endpoint a harness is pointed at

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D007` - deferred harnesses must remain addable
- `DECISIONS-LmProxy-agent-launcher.md#D010` - hybrid configuration channels expressed as actions
- `DECISIONS-LmProxy-agent-launcher.md#D014` - adapter-declared model requirement
- `DECISIONS-LmProxy-agent-launcher.md#D020` - harnesses may be CLI or GUI apps
- `DECISIONS-LmProxy-agent-launcher.md#T005` - one adapter interface plus a static metadata record per harness
- `DECISIONS-LmProxy-agent-launcher.md#T019` - HarnessMetadata record shape
- `DECISIONS-LmProxy-agent-launcher.md#T020` - adapters plan, Core executes
- `DECISIONS-LmProxy-agent-launcher.md#T023` - closed three-variant action family
- `DECISIONS-LmProxy-agent-launcher.md#T024` - plan carries CliInvoke ProcessConfiguration

## Acceptance criteria

- [ ] HarnessMetadata carries Name, Kind, RequiresModel, InstallHint, optional DetectionCommand, and ConfigFileCandidates; null DetectionCommand means PATH probe of the launch command
- [ ] IHarnessAdapter exposes Metadata, IsAvailableAsync, and PlanLaunchAsync returning a LaunchPlan; adapters stay pure planners
- [ ] ConfigurationAction is a closed family of exactly CliArgumentAction, ConfigFileWriteAction, and EnvironmentVariableAction
- [ ] LaunchRequest is (Backend, Model?, WorkingDirectory); LaunchPlan carries a CliInvoke ProcessConfiguration plus the action list
- [ ] The seam compiles against CliInvoke 3.0.0-beta.3 and admits new adapters without interface changes

## Dependencies

**Blocked by** - 002-settings-domain-and-store
