---
title: Copilot CLI and Pi adapters
classification: Independent
blocked_by: [004-adapter-and-launch-contracts]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Implement the adapter contract for GitHub Copilot CLI and Pi - the remaining two version 1 harnesses - with accurate metadata and hybrid launch planning.

## What to build

- `Adapters/CopilotCliAdapter.cs` and `Adapters/PiAdapter.cs`, each declaring HarnessMetadata with the kind (CLI or GUI) matching the harness's real nature, RequiresModel matching the harness's real model behavior, an accurate InstallHint, DetectionCommand (or null to probe the launch command on PATH), and ConfigFileCandidates.
- `PlanLaunchAsync` builds the hybrid: EnvironmentVariableAction and CliArgumentAction where the harness honors them; ConfigFileWriteAction only for what is genuinely required. Detection and launch mechanics must handle both CLI and GUI kinds.
- Undeclared-model behavior defaults to warn-and-launch: a harness whose metadata does not require a model may launch without one.
- Dummy-key behavior documented per adapter in code comments - a dummy value is supplied only where the harness rejects empty keys.
- Tests: planning against temp directories; availability detection with scripted fakes (available and unavailable cases).

## Size

- **Files** - 4 (2 source, 2 test)
- **Large Edits required**

## Recommended Workflow

### Step 1 - Author GitHub Copilot CLI metadata

Where: `src/LmProxy.AgentLauncher.Core/Adapters/CopilotCliAdapter.cs`

- Declare kind, RequiresModel, install hint, detection command, and config-file candidates.

Verify: metadata reviewed against the harness's documented behavior.

### Step 2 - Implement GitHub Copilot CLI launch planning

Where: `src/LmProxy.AgentLauncher.Core/Adapters/CopilotCliAdapter.cs`

- Prefer environment variables and CLI arguments; add config-file writes only where required.

Verify: a temp-dir planning test asserts the expected action sequence.

### Step 3 - Author Pi metadata and planning

Where: `src/LmProxy.AgentLauncher.Core/Adapters/PiAdapter.cs`

- Same metadata and planning work for Pi.

Verify: the same temp-dir planning test passes for Pi.

### Step 4 - Availability detection tests

Where: `src/LmProxy.AgentLauncher.Tests/Adapters/`

- Scripted fakes covering available and unavailable paths for both adapters.

Verify: unavailable harnesses report the install hint path per the error contract.

### Step 5 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher.Core/Adapters/` - target folder
- `src/LmProxy.AgentLauncher.Core/Adapters/IHarnessAdapter.cs` - the contract being implemented

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Adapter - the per-harness component that detects, configures, and starts one harness
- Harness - GitHub Copilot CLI and Pi in this ticket
- Configuration action - the planned env/arg/file steps
- Backend - the endpoint the plan points the harness at

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D005` - version 1 harness set includes GitHub Copilot CLI and Pi
- `DECISIONS-LmProxy-agent-launcher.md#D010` - hybrid configuration channels
- `DECISIONS-LmProxy-agent-launcher.md#D012` - dummy key only where the harness rejects empty keys
- `DECISIONS-LmProxy-agent-launcher.md#D014` - adapter-declared model requirement; warn-and-launch default
- `DECISIONS-LmProxy-agent-launcher.md#D018` - install hint surfaced before any configuration action
- `DECISIONS-LmProxy-agent-launcher.md#D020` - harnesses may be CLI or GUI apps
- `DECISIONS-LmProxy-agent-launcher.md#T019` - HarnessMetadata record shape

## Acceptance criteria

- [ ] Both adapters declare accurate metadata (kind, RequiresModel, InstallHint, DetectionCommand or null, ConfigFileCandidates)
- [ ] Plans use env/arg actions where honored and minimal ConfigFileWriteAction otherwise
- [ ] Detection and launch mechanics handle the declared kind (CLI or GUI) for each harness
- [ ] Dummy-key behavior is documented per adapter and applied only where required
- [ ] Temp-dir planning tests and availability tests pass

## Dependencies

**Blocked by** - 004-adapter-and-launch-contracts
