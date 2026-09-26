---
title: --backend resolution and profile-aware reads
classification: Independent
blocked_by: ["launch/005-protocol-compat-enforcement.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Teach direct launch to select a saved backend entry by name (`--backend <name>`), layer per-launch overrides field by field, and adopt the profile-aware read policy in the launch path - with the unknown/dangling-name failure class (exit 8) as the documented contract.

## What to build

- Add a `--backend <name>` long option to `src/HarnessGateway.Launcher/Commands/LaunchCommand.cs` and thread the value through the root action in `src/HarnessGateway.Launcher/Program.cs` into `LaunchOrchestrator.RunAsync`.
- Resolution lives in Core (new shared resolver, for example `src/HarnessGateway.Launcher.Core/Backends/BackendResolution.cs`), so the `config backend` verbs in TK009 can reuse it without climbing into command-layer code. Behaviour per T029 - `--backend` selects a saved entry; a given `--endpoint`, `--protocol`, or `--model` overrides only that field on the resolved entry; raw-URL rule requires `--endpoint` and `--protocol` together (exit 2 otherwise); no protocol probing.
- Profile-aware reads per T033 - with no `--backend`, resolve the `DefaultBackend` pointer only. No auto-promotion and no first-entry fallback when multiple entries exist (remove today's `FirstOrDefault()` fallback). A pointer naming nothing (dangling) or an empty pointer with existing entries fails with exit code 8 and a message directing the user to `config backend switch` - per decision-ledger interaction record I037 (reuse of code 8 confirmed by the user).
- Add exit code 8 (`UnknownBackendName`) to `src/HarnessGateway.Launcher/Commands/LaunchExitCode.cs` and append its README row. Unknown-name messages may carry best-effort close-match hints; those are non-contractual (T038) - never gate outcomes, never appear in the exit-code table.
- Keys are not overridable per launch - do not add any `--key` surface on the launch path (T036); the resolved entry's stored key is the sole source.
- The `--persist` family stays closed at `config|model`; a `--backend` launch persists nothing about its selection (T042). Negative assertion covered by the usage-error tests.
- README - new code-8 row plus documentation of `--backend` field-level layering.

## Size

- Files - 7 (command, root wiring, orchestrator, new resolver, exit enum, README, tests)

## Recommended Workflow

### Step 1 - Core resolver with layering

Where: src/HarnessGateway.Launcher.Core/Backends/BackendResolution.cs (new)

- Implement named-entry resolution with per-field override layering, raw-URL pairing rule, strict default-pointer reads, and the unknown/dangling/absent-default failure results (typed, not exits).

Verify: layering precedence tests - flag values win field-by-field; raw pair rule errors.

### Step 2 - Wire the option and orchestrator

Where: src/HarnessGateway.Launcher/Commands/LaunchCommand.cs, src/HarnessGateway.Launcher/Program.cs, src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs

- Add the option, thread it, and delegate resolution to the Core resolver. Preserve self-heal ordering and single-write defaults.

Verify: orchestrator tests - per-field overrides, unknown name exit 8, dangling/absent default exit 8 with the `config backend switch` pointer.

### Step 3 - Contract and documentation

Where: src/HarnessGateway.Launcher/Commands/LaunchExitCode.cs, README.md

- Add code 8; append the README row (unresolvable backend-name references, including the default pointer); document `--backend` layering and the no-key-override rule.

Verify: `dotnet test --solution src/HarnessGateway.slnx` passes; README rows complete.

## Context pointers

##### Files

- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - current resolution still carries a fallback to remove
- src/HarnessGateway.Launcher/Commands/LaunchCommand.cs - option surface
- src/HarnessGateway.Launcher.Core/Settings/SettingsStore.cs - settings source
- README.md - contract table and usage

##### ADRs

- Decision ledger only.

##### Domain terms

- Backend, Default endpoint.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T029 - flag resolution and field layering
- DECISIONS-LmProxy-agent-launcher.md#T031, T038 - code 8 and non-contractual hints
- DECISIONS-LmProxy-agent-launcher.md#T033 - default-pointer read rule and dangling error (exit code per I037)
- DECISIONS-LmProxy-agent-launcher.md#T036, T042 - no key override; persist family closed
- DECISIONS-LmProxy-agent-launcher.md#T010, T017, T011, T015, D009 - raw-URL rule, protocol values, schema shape, persist semantics, default endpoint
- DECISIONS-LmProxy-agent-launcher.md#I037 - user-confirmed code-8 reuse

## Acceptance criteria

- [ ] `--backend` resolves a saved entry; per-field overrides apply; raw-URL rule still requires endpoint and protocol together (exit 2)
- [ ] No `--key` override exists on any launch path
- [ ] Unknown `--backend` name exits 8; dangling or absent default with entries present also exits 8 and directs to `config backend switch` (I037)
- [ ] First-entry fallback removed; multiple-entry files read the default pointer only
- [ ] `--persist` still accepts exactly `config|model`; a `--backend` launch cannot persist its selection
- [ ] LaunchExitCode enum and README row updated together and exit 8's message contract documented

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - launch/005-protocol-compat-enforcement.md (same-file launch orchestrator and README chain.
