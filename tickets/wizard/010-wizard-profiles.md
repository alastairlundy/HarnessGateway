---
title: Wizard profile awareness
classification: Independent
blocked_by: ["adapters/001-harness-metadata-supported-protocols.md", "launch/006-backend-resolution.md", "settings/007-secure-key-store.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Keep the wizard's familiar D019 step order while absorbing profile-aware backend reads and the harness/backend protocol-compatibility check, with incompatible pairings re-prompting annotated at the harness step.

## What to build

- Preserve the locked step order in `src/HarnessGateway.Launcher/Wizard/WizardPipeline.cs` - harness, then endpoint, then key if needed, then model - resolving the backend from the default pointer at start (no third step; no parallel prompting).
- After harness selection, check the selected harness's `SupportedProtocols` (TK001 field) against the resolved backend's protocol. Incompatible pairings re-prompt the harness choice with an annotation explaining why (the direct-mode exit-3 pre-check counterpart is interactive here, not a new contract).
- Prefills - the endpoint step pre-fills from the resolved default entry's base URL and the model step pre-fills from its default model (the strict default-pointer read policy comes from TK006). The wizard launch uses the resolved default backend for the whole launch; per-launch `--backend` remains direct-mode-only.
- Warn-and-launch applies at the model step per T027 when no model-requirement declaration exists - the wizard treats the harness as skippable there (the locking tests for that behaviour are in TK011; this ticket only needs to keep the step wiring compatible with that change).
- The wizard stays write-free end to end and continues to surface saved keys through the in-memory handoff (using the secure-store reference semantics from TK007). Wizard I/O stays through `IConsoleAbstraction` with scripted-input tests.

## Size

- Files - 3 (pipeline, step wiring, tests)

## Recommended Workflow

### Step 1 - Resolve the default backend up front

Where: src/HarnessGateway.Launcher/Wizard/WizardPipeline.cs

- Use the Core resolution from TK006 (no command-layer dependency in the pipeline), so profile reads and strict default-pointer rules are consistent in the wizard.

Verify: prefill tests - endpoint value and model default come from the resolved entry.

### Step 2 - Harness-step guard

Where: WizardPipeline.cs (and any step extraction out of the pipeline)

- After harness choice, evaluate compatibility; on failure re-prompt with an annotation naming the incompatible protocol. The annotation mirrors the direct-mode failure reason and the wizard never writes settings.

Verify: scripted tests - a mismatched pairing re-prompts; the compatible pairing proceeds; the flow has zero settings writes throughout.

### Step 3 - Model-step and key-step structural alignment

Where: WizardPipeline.cs, Wizard/ModelStep.cs

- Keep the D019 order; adopt the secure-store reference semantics from TK007 in the key step (in-memory secret only; never persisted).

Verify: step order test with scripted inputs reflects harness; endpoint; key when needed; model.

## Context pointers

##### Files

- src/HarnessGateway.Launcher/Wizard/WizardPipeline.cs - step pipeline
- src/HarnessGateway.Launcher/Wizard/ModelStep.cs - model step kept aligned to D014 skip gating
- src/HarnessGateway.Launcher.Tests/Wizard/ - scripted-input test scaffolding

##### ADRs

- Decision ledger only.

##### Domain terms

- Harness, Adapter, Backend, Default endpoint.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T034 - the wizard record (step order, re-prompt, no third step)
- DECISIONS-LmProxy-agent-launcher.md#T033, T028, T013, T016 - reads, pairing enforcement, prefill context, wizard write-free
- DECISIONS-LmProxy-agent-launcher.md#D019, D014, D013 - step order and skip gating
- DECISIONS-LmProxy-agent-launcher.md#T015, T016, T040, T005, T012 - per-launch persist semantics, write scope, secure reference semantics, adapter architecture, no new code

## Acceptance criteria

- [ ] Steps run in the locked order harness, endpoint, key when needed, model
- [ ] Incompatible harness/backend pairing re-prompts with an annotation; no third step
- [ ] Prefills come strictly from the resolved default entry, including endpoint and default model
- [ ] The wizard remains write-free; no settings state is stored or mutated at any step

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - adapters/001-harness-metadata-supported-protocols.md, launch/006-backend-resolution.md, settings/007-secure-key-store.md.
