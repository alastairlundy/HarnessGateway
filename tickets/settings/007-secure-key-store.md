---
title: Secure key store
classification: Independent
blocked_by: []
parent: IMPLEMENTATION-TODO.md
---

## Goal

Move saved API keys out of plaintext settings JSON and into the platform-native credential store behind one Core abstraction, with settings referencing keys rather than containing them.

## What to build

- New `ISecureKeyStore` abstraction (store, retrieve, delete - reference in, reference out at the JSON level) in the Core Settings/Security area.
- Three implementations covering all release OS families
  - Windows - DPAPI (Data Protection API) via the official `System.Security.Cryptography.ProtectedData` package (CPM entry; no `Version=` on the `PackageReference`)
  - macOS - the `security` command-line utility (generic password) or an equivalent local API route
  - Linux - Secret Service through `secret-tool` or an equivalent libsecret-based route
- Semantics flip on `src/HarnessGateway.Launcher.Core/Backends/Backend.cs` - `ApiKey` now holds a secure-store reference; the JSON shape stays byte-compatible with today's schema (T040).
- Retrieval wiring - the orchestrator's env-var actions and the wizard's key step resolve the reference to the actual secret through the store; the reference material must never be emitted to a harness process.
- Existing `config` verbs that set and clear the saved key (`src/HarnessGateway.Launcher/Commands/ConfigCommand.cs`) route through the store: writing a key stores the secret and persists the reference; clearing removes the secret and nulls the reference.
- Tests use a fake-backed store per the fakes strategy - no real keychain interaction in the test gate.
- The three implementations ship in this phase, but the abstraction is the only surface other code consumes (T040 - one storage abstraction).

## Size

- Files - 8 (abstraction plus three per-OS implementations, Backend record, ConfigCommand rewire, orchestrator and wizard touchpoints, tests)

## Recommended Workflow

### Step 1 - Define the abstraction

Where: src/HarnessGateway.Launcher.Core/Settings/ (or Security/) ISecureKeyStore.cs (new)

- Store/retrieve/delete keyed by a reference; reference format must be distinguishable in JSON without leaking the secret.

Verify: abstraction compiles and the shape has no plaintext fields.

### Step 2 - Three per-OS implementations

Where: three implementation files alongside the abstraction

- DPAPI via ProtectedData package (CPM), macOS via the `security` utility or equivalent, Linux Secret Service via `secret-tool` or equivalent. Each guarded by OS checks; unsupported-at-runtime surfaces produce instructive errors.

Verify: each implementation stores and retrieves through its native store on its own OS family.

### Step 3 - Retrofit the record and read/write paths

Where: Backend.cs, ConfigCommand.cs, wizard key step, orchestrator env actions

- `ApiKey` semantics become reference-only at rest; resolve to secrets at launch and at the wizard in-memory handoff only; existing config set/clear flows route through the store.

Verify: no settings JSON fixture gains or keeps literal secret material; config flows still round-trip a key.

### Step 4 - Fake-backed tests

Where: src/HarnessGateway.Launcher.Tests/

- Exercisable end-to-end against a fake store; three real implementations covered by construction-level tests where possible plus review-level OS checks outside the gate.

Verify: `dotnet test --solution src/HarnessGateway.slnx` passes fake-backed.

## Context pointers

##### Files

- src/HarnessGateway.Launcher.Core/Backends/Backend.cs - record whose ApiKey semantics change
- src/HarnessGateway.Launcher/Commands/ConfigCommand.cs - set/clear-saved-key flows
- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - env-var action execution
- src/HarnessGateway.Launcher/Wizard/WizardPipeline.cs - key step consumes the saved reference
- src/Directory.Packages.props - CPM entry for the ProtectedData package

##### ADRs

- Decision ledger only.

##### Domain terms

- Backend - record shape context.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T040 - this record is the core spec; three OS families, one abstraction, JSON has no secret material
- DECISIONS-LmProxy-agent-launcher.md#T018 - ApiKey semantics superseded in part by T040
- DECISIONS-LmProxy-agent-launcher.md#D012 - optional key handling preserved (no dummy values)
- DECISIONS-LmProxy-agent-launcher.md#T003 - the three OS families map onto the release channel targets

## Acceptance criteria

- [ ] `ISecureKeyStore` is the single surface; settings JSON never contains secret material
- [ ] DPAPI (Data Protection API), Keychain and Secret Service implementations exist; the Windows one goes through CPM
- [ ] Launch-time env-var actions and wizard key handling resolve references to secrets internally - the reference never reaches a harness process
- [ ] Existing config saved-key set and clear verbs route through the store
- [ ] Fake-backed tests pass; no plaintext-key fixture remains in the gate

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - None - can start immediately.
