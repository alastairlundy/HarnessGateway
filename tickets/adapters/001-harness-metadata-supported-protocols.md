---
title: HarnessMetadata SupportedProtocols amendment
classification: Independent
blocked_by: []
parent: IMPLEMENTATION-TODO.md
---

## Goal

Amend the shared adapter metadata record with a `SupportedProtocols` field so every harness declares the set of protocols it can target. This is the type-surface seed for the Phase 2 enforcement (direct mode exit 3) and wizard re-prompt behaviour.

## What to build

- Extend `src/HarnessGateway.Launcher.Core/Adapters/HarnessMetadata.cs` with a `SupportedProtocols` field (the set of `ProtocolKind` values the harness can target). Keep all existing fields unchanged - the amendment is purely additive.
- Populate the field for the four existing adapters (`OpenCodeAdapter`, `KiloAdapter`, `CopilotCliAdapter`, `PiAdapter`) by inspecting each adapter's `PlanLaunchAsync` protocol branches: every protocol family the planner special-cases today is listed. Do not narrow or widen any current behaviour.
- Update the metadata construction in `src/HarnessGateway.Launcher.Tests/Wizard/WizardTestHelper.cs` and any metadata fixtures so tests still compile.
- Extend the adapter contract tests to assert each adapter's declared protocol set.

## Size

- Files - 7 (4 adapters, 1 record, 1 wizard test fixture, adapter tests)

## Recommended Workflow

### Step 1 - Add the field to the metadata record

Where: src/HarnessGateway.Launcher.Core/Adapters/HarnessMetadata.cs

- Add the `SupportedProtocols` positional parameter or property typed as the protocol values set.
- Keep the existing parameter names and order intact.

Verify: the solution still builds (`dotnet build --solution src/HarnessGateway.slnx`).

### Step 2 - Populate per-adapter values

Where: src/HarnessGateway.Launcher.Core/Adapters/*.cs

- Read each adapter's `PlanLaunchAsync` and list the `ProtocolKind` values it already handles in its switch or env-var emission.
- Update each adapter's `Metadata` initialiser.

Verify: no launch-path change for the four v1 harnesses; each listed protocol was already handled by that planner.

### Step 3 - Update test fixtures and contracts

Where: src/HarnessGateway.Launcher.Tests/Adapters/, src/HarnessGateway.Launcher.Tests/Wizard/WizardTestHelper.cs

- Adjust every place that constructs `HarnessMetadata`; add assertions on the new values.

Verify: `dotnet test --solution src/HarnessGateway.slnx` passes.

## Context pointers

##### Files

- src/HarnessGateway.Launcher.Core/Adapters/HarnessMetadata.cs - the record being amended
- src/HarnessGateway.Launcher.Core/Backends/ProtocolKind.cs - the value space the new field draws from
- src/HarnessGateway.Launcher.Tests/Wizard/WizardTestHelper.cs - constructs metadata outside adapter tests

##### ADRs

- Decision ledger only; `docs/adr/` does not exist in this repository.

##### Domain terms

- Harness, Adapter, Backend - as defined in GLOSSARY.md.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T044 - additive HarnessMetadata amendment; enforcing sites cite T028 (direct) and T034 (wizard)
- DECISIONS-LmProxy-agent-launcher.md#T019 - the record shape being extended
- DECISIONS-LmProxy-agent-launcher.md#T005 - adapter architecture the record belongs to
- DECISIONS-LmProxy-agent-launcher.md#D001 - Phase 2 scope translation anchor for the reused foundation

## Acceptance criteria

- [ ] `HarnessMetadata` carries `SupportedProtocols` and no existing field changed position or type
- [ ] All four v1 adapters compile and declare exactly the protocols their planner already branches on
- [ ] `dotnet test --solution src/HarnessGateway.slnx` passes with the new fixture values and contract assertions
- [ ] v1 launch behaviour is unchanged (no protocol family gained or lost handling)

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - None - can start immediately.
