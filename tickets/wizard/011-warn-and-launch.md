---
title: Warn-and-launch confirmation
classification: Independent
blocked_by: ["wizard/010-wizard-profiles.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Lock the warn-and-launch behaviour for harnesses with no model-requirement declaration as a confirmed cross-adapter rule, and keep the real-harness integration matrix deferred this phase.

## What to build

- Direct mode - when a harness's model-requirement status cannot be determined, print a visible warning that no requirement is declared and launch without a model. No new exit code (a warning is not a failure class). Today all adapters declare a definite value, so the closed set never hits the undeclared branch; the work here is confirming that contract and locking it under test, not inventing a partial-declaration type change (the metadata amendment in TK001 is purely the SupportedProtocols field - per record T044).
- Wizard - the same warning at the model step, with such a harness treated as skippable.
- Tests with fake adapters lock both surfaces, and the adapter contract tests state the closed set always declares a definite model requirement. Any adapter carrying no declaration in future would fall through to warn-and-launch without a code change - verify that reading against the shared metadata type and the orchestrator's warn dispatch.
- The real-harness integration test matrix remains a deferred TODO entry; this phase ships fakes-only (per T043's record) - don't add real harness runs to the gate.

## Size

- Files - 3 (adapter contract tests, orchestrator test additions, wizard test additions)

## Recommended Workflow

### Step 1 - Lock the direct-mode surface

Where: src/HarnessGateway.Launcher.Tests/Commands/LaunchOrchestratorTests.cs

- Confirm the warn-then-launch path through the orchestrator with fake adapter metadata (no new exit code asserted), plus a regression test that declared harnesses warn only under the existing declared-required rule.

Verify: no new exit codes in the enum; warning surfaces before launch, never blocks it.

### Step 2 - Lock the wizard surface

Where: src/HarnessGateway.Launcher.Tests/Wizard/

- Model-step warning visible with skip permitted; declared harness unchanged.

Verify: scripted-input tests hold.

### Step 3 - Document the contract in adapter contract tests

Where: src/HarnessGateway.Launcher.Tests/Adapters/AdapterContractsTests.cs

- Assert every shipped adapter declares a definite model requirement, keeping the undeclared path the only future-facing branch.

Verify: contract tests pass.

## Context pointers

##### Files

- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - warn dispatch lives in model resolution
- src/HarnessGateway.Launcher/Wizard/ModelStep.cs - wizard model step
- src/HarnessGateway.Launcher.Core/Adapters/HarnessMetadata.cs - declaration field
- TODO.md - Testing section (the matrix stays open; T043 defers it)

##### ADRs

- Decision ledger only.

##### Domain terms

- Adapter, Harness.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T027 - the record locked here
- DECISIONS-LmProxy-agent-launcher.md#D013, D014 - declared behaviour unchanged
- DECISIONS-LmProxy-agent-launcher.md#T019, T042, T043 - metadata record, no exit-code drift, matrix deferral
- DECISIONS-LmProxy-agent-launcher.md#T009 - fakes-first testing stance

## Acceptance criteria

- [ ] Direct mode warns and launches for a harness with a missing declaration; no new exit code
- [ ] Wizard shows the same warning at the model step and treats it as skippable
- [ ] Declared harnesses keep their exact behaviour (wizard skip gating, direct-mode required-model warning) with regression tests
- [ ] The real-harness integration matrix remains a deferred TODO (no real-harness runs in this gate)

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - wizard/010-wizard-profiles.md (shared wizard step wiring and test file chain).
