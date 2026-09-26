---
title: -m short alias for --model
classification: Independent
blocked_by: ["settings/009-backend-verbs.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Accept `-m` as the sole short alias of `--model` on direct launch - no other short flags ship this phase.

## What to build

- Add `-m` to the `--model` option in `src/HarnessGateway.Launcher/Commands/LaunchCommand.cs` (System.CommandLine alias on the existing option), keeping `--endpoint`, `--protocol`, `--persist`, and the new `--backend` long-flag-only.
- No other short aliases are defined for launch or config verbs. The single-alias set carries no collision risk (T041).
- Parsing tests - `-m` behaves identically to `--model` (value precedence on the resolved backend's default model comes from the TK006 layering, unchanged).
- README usage line documents the alias.

## Size

- Files - 3 (command, tests, README)

## Recommended Workflow

### Step 1 - Add the alias

Where: src/HarnessGateway.Launcher/Commands/LaunchCommand.cs

- Extend the `--model` option with a `-m` alias.

Verify: `-m some-model` parses through the launch path.

### Step 2 - Tests and docs

Where: src/HarnessGateway.Launcher.Tests/Commands/ (parsing test), README.md

- Parsing equivalence test plus README usage line.

Verify: `dotnet test --solution src/HarnessGateway.slnx` passes.

## Context pointers

##### Files

- src/HarnessGateway.Launcher/Commands/LaunchCommand.cs - option surface
- README.md - usage line

##### ADRs

- Decision ledger only.

##### Domain terms

- Backend, Default endpoint.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T041 - the sole-alias record
- DECISIONS-LmProxy-agent-launcher.md#T010, T029 - the long-flag contract it amends minimally

## Acceptance criteria

- [ ] `-m` is equivalent to `--model` on direct launch and unspecified on every config verb
- [ ] No other short aliases were introduced
- [ ] README `-m` documentation present

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - settings/009-backend-verbs.md (README same-file chain terminus before this ticket).
