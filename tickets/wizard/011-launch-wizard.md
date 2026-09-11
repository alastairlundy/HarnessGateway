---
title: Launch wizard
classification: Independent
blocked_by: [003-model-catalog-client, 009-direct-launch-path]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Build the no-args `launch` wizard - harness selection, endpoint, key, and model steps rendered with Spectre.Console - applying collected values to the current launch only and never writing settings.

## What to build

- `Wizard/WizardPipeline.cs` - step order: harness - endpoint (prefilled from the saved default; a change applies once for this launch) - key (only when needed and unsaved; asks but never stores) - model. Unavailable harnesses are annotated in the harness list. The wizard never writes settings.
- `Wizard/ModelStep.cs` - picker populated from the Model catalog with an explicit skip option; skip is blocked when the adapter's RequiresModel is true; prefill the saved per-backend default model when one exists; when the endpoint is unreachable the step degrades gracefully (skip or manual entry) rather than failing the launch.
- Spectre.Console renders and prompts; wizard step logic stays framework-agnostic plain code, tested through a testable console abstraction.
- Wire the no-args `launch` branch to the wizard; the collected values funnel into the same orchestrator pipeline as direct launches (launch-only values).
- Tests: step logic with a testable console; skip gating; prefill; degradation; a no-settings-writes assertion.

## Size

- **Files** - 6 (WizardPipeline.cs, ModelStep.cs, console abstraction, Program.cs wiring, 2 test files)
- **Large Edits required**

## Recommended Workflow

### Step 1 - Console abstraction and step scaffolding

Where: `src/LmProxy.AgentLauncher/Wizard/`

- Create a testable console abstraction and the pipeline skeleton with the four steps in order.

Verify: steps run headlessly in tests.

### Step 2 - Harness step with availability annotation

Where: `src/LmProxy.AgentLauncher/Wizard/WizardPipeline.cs`

- List harnesses with unavailable ones annotated.

Verify: a test asserts unavailable harnesses are marked in the list.

### Step 3 - Endpoint step

Where: `src/LmProxy.AgentLauncher/Wizard/WizardPipeline.cs`

- Prefill from the saved default; a changed value applies once for this launch.

Verify: prefill test passes; no settings writes occur.

### Step 4 - Key step

Where: `src/LmProxy.AgentLauncher/Wizard/WizardPipeline.cs`

- Ask only when the backend needs a key and none is saved; never store the answer.

Verify: a test asserts no settings writes.

### Step 5 - Model step

Where: `src/LmProxy.AgentLauncher/Wizard/ModelStep.cs`

- Catalog picker with explicit skip; skip blocked when RequiresModel is true; prefill the saved default model; degrade gracefully when the endpoint is unreachable.

Verify: branch tests for each case.

### Step 6 - Wire no-args launch to the wizard

Where: `src/LmProxy.AgentLauncher/Program.cs`, `src/LmProxy.AgentLauncher/Commands/`

- Route the no-args `launch` branch into the wizard and funnel the result through the orchestrator pipeline.

Verify: an end-to-end fake test launches through the pipeline.

### Step 7 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher/Wizard/` - target folder
- `src/LmProxy.AgentLauncher/Commands/LaunchOrchestrator.cs` - the pipeline the wizard funnels into
- `src/LmProxy.AgentLauncher.Core/Backends/Models/ModelCatalogClient.cs` - feeds the model picker

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Harness - what the wizard's first step selects
- Adapter - the source of RequiresModel and availability facts
- Backend - the endpoint the wizard collects
- Default endpoint - the prefill source for the endpoint step
- Model catalog - what populates the model picker

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D004` - the no-args wizard entrypoint
- `DECISIONS-LmProxy-agent-launcher.md#D013` - model picker from the models listing; graceful degradation
- `DECISIONS-LmProxy-agent-launcher.md#D014` - skip gated by the adapter declaration
- `DECISIONS-LmProxy-agent-launcher.md#D018` - unavailable harnesses annotated
- `DECISIONS-LmProxy-agent-launcher.md#D019` - step order harness - endpoint - key - model
- `DECISIONS-LmProxy-agent-launcher.md#T004` - Spectre.Console rendering; framework-agnostic step logic
- `DECISIONS-LmProxy-agent-launcher.md#T013` - wizard values apply to the current launch only; never writes settings
- `DECISIONS-LmProxy-agent-launcher.md#T014` - wizard prefills the saved default model
- `DECISIONS-LmProxy-agent-launcher.md#T016` - the wizard never writes settings

## Acceptance criteria

- [ ] The wizard walks harness - endpoint - key - model in that order
- [ ] The endpoint step prefills from the saved default and applies a changed value once for the current launch
- [ ] The key step asks only when needed and unsaved, and never stores
- [ ] The model step offers a catalog picker with skip gated by RequiresModel, prefills the saved default model, and degrades gracefully when the endpoint is unreachable
- [ ] Unavailable harnesses are annotated in the harness list
- [ ] The wizard never writes settings; step-logic tests with a testable console pass

## Dependencies

**Blocked by** - 003-model-catalog-client, 009-direct-launch-path
