---
title: Direct-mode protocol-compat enforcement
classification: Independent
blocked_by: ["adapters/001-harness-metadata-supported-protocols.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Make the direct launch path refuse harness/backend pairings whose protocols cannot possibly work - failing with exit code 3 before any configuration action.

## What to build

- In `src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs`, after adapter selection and before any backup, self-heal ordering change or `PlanLaunchAsync`, compare the resolved backend's `Protocol` against `adapter.Metadata.SupportedProtocols` (the field from TK001). On mismatch, print an explanatory error naming both the harness and the incompatible protocol, and return exit code 3.
- Ordering - the check must run before any configuration action; the existing unknown-agent and unavailable-harness checks keep their current order and codes.
- Broaden the code-3 row label in the README exit-code table to cover unknown agent and invalid agent-backend selection (broadens the documented contract; no numeric change).
- The `UnknownAgent` enum member stays named and valued as-is; only the README label and the orchestrator error path use the broadened meaning.

## Size

- Files - 4 (orchestrator, README, tests; no enum change needed)

## Recommended Workflow

### Step 1 - Insert the compatibility check

Where: src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs

- After adapter lookup and availability check, before planning. Error text names harness plus protocol.

Verify: a fake-adapter test with a mismatched backend protocol exits 3 and writes nothing.

### Step 2 - Update the README contract row

Where: README.md

- Broaden the code-3 row label only; leave every other row untouched.

Verify: README row wording matches the new failure class.

### Step 3 - Cover the failure path

Where: src/HarnessGateway.Launcher.Tests/Commands/LaunchOrchestratorTests.cs

- Incompatible pairing exits 3 before any config action; compatible pairings still launch.

## Context pointers

##### Files

- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - the launch pipeline
- src/HarnessGateway.Launcher.Commands/LaunchExitCode.cs - contract constants (unchanged numerically)
- README.md - exit-code table

##### ADRs

- Decision ledger only.

##### Domain terms

- Backend, Adapter, Harness.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T028 - enforcement, exit 3, label broadening
- DECISIONS-LmProxy-agent-launcher.md#T012 - append-only exit-code contract
- DECISIONS-LmProxy-agent-launcher.md#T017, T044 - protocol values and metadata field
- DECISIONS-LmProxy-agent-launcher.md#D008 - backend definition

## Acceptance criteria

- [ ] Incompatible harness/backend pairings fail with exit 3 before any configuration action
- [ ] README code-3 row label is broadened to unknown agent and invalid agent-backend selection
- [ ] Existing exit-2/3/4 paths keep their codes and ordering
- [ ] Failure-path tests pass with fake adapters and do not perform any write

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - adapters/001-harness-metadata-supported-protocols.md.
