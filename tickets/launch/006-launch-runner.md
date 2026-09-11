---
title: Launch runner
classification: Independent
blocked_by: [004-adapter-and-launch-contracts]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Run the planned harness process directly through CliInvoke in the foreground with inherited standard streams, exiting with the harness's exit code - matching how `ollama launch` behaves.

## What to build

- `Execution/LaunchRunner.cs` - executes the plan's CliInvoke ProcessConfiguration directly - no ProcessStartInfo round-trip. Foreground and blocking, with inherited standard input/output/error streams (stdio applies to CLI harnesses; GUI kinds launch without stdio expectations). The runner surfaces the harness exit code to the caller.
- The runner exposes launch completion so the orchestrator can restore backups in try/finally and signal handlers; restore ownership stays with the backup service and the orchestrator, not the runner.
- Tests: a scripted fake harness (a script exiting with a known code) - the exit code propagates; output is inherited and observable.

## Size

- **Files** - 3 (1 source, 2 test)

## Recommended Workflow

### Step 1 - Implement foreground execution

Where: `src/LmProxy.AgentLauncher.Core/Execution/LaunchRunner.cs`

- Execute the plan's ProcessConfiguration via CliInvoke, blocking until exit.

Verify: a fake-harness test runs to exit.

### Step 2 - Propagate the harness exit code

Where: `src/LmProxy.AgentLauncher.Core/Execution/LaunchRunner.cs`

- Surface the harness's own exit code as the runner's result.

Verify: a test asserts a known non-zero exit code surfaces unchanged.

### Step 3 - Verify inherited stdio

Where: `src/LmProxy.AgentLauncher.Core/Execution/LaunchRunner.cs`

- Confirm the child's standard streams are inherited for CLI harnesses.

Verify: fake-harness output is observable in the test.

### Step 4 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher.Core/Execution/` - target folder
- `src/LmProxy.AgentLauncher.Core/Launch/LaunchPlan.cs` - the plan whose Process this runner executes

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Harness - the process being launched in the foreground

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D015` - block-and-inherit foreground execution; launcher exits with the harness exit code
- `DECISIONS-LmProxy-agent-launcher.md#T008` - interrupt-handling context the runner must accommodate
- `DECISIONS-LmProxy-agent-launcher.md#T024` - direct ProcessConfiguration execution; CliInvoke beta churn re-opens this contract

## Acceptance criteria

- [ ] The plan's ProcessConfiguration executes directly via CliInvoke with no ProcessStartInfo round-trip
- [ ] Execution is foreground and blocking with inherited stdio for CLI harnesses
- [ ] The launcher surfaces the harness's exit code unchanged
- [ ] Fake-harness tests for exit-code propagation and stream inheritance pass

## Dependencies

**Blocked by** - 004-adapter-and-launch-contracts
