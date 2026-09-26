---
title: Claude Code adapter
classification: Independent
blocked_by: ["adapters/001-harness-metadata-supported-protocols.md", "adapters/003-codex-adapter.md"]
parent: IMPLEMENTATION-TODO.md
---

## Goal

Add Claude Code as a launcher target configured exclusively through environment variables and an optional `--model` argument - no config-file writes - so the user's durable Claude Code settings are never touched.

## What to build

- New `src/HarnessGateway.Launcher.Core/Adapters/ClaudeCodeAdapter.cs` following the existing adapter shape.
- Metadata - name `claude-code`, kind Cli, does not require a model, supports only the anthropic protocol, no config-file candidates, detection probing `claude` on PATH, npm install hint.
- `PlanLaunchAsync` emits environment-variable actions only - `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN` (only when the resolved backend entry has a saved key), and `ANTHROPIC_MODEL` (only when a model was chosen) - plus an optional `--model` CLI argument action. Never a config-file write.
- Token variable note - use `ANTHROPIC_AUTH_TOKEN` rather than `ANTHROPIC_API_KEY`; the latter triggers an interactive approval prompt in the harness. Encode that choice in a comment and a test so it does not regress.
- Install hint documents that pointing the tool at a non-first-party base URL disables MCP (Model Context Protocol) tool search and Remote Control - a consequence of the harness, not an action by the launcher (recorded in the adapter warning text per T026).
- Key handling - inject the saved key only when present, per the no-dummy rule; when no key is saved, no token action at all.
- Register in the adapter list in `src/HarnessGateway.Launcher/Program.cs`.

## Size

- Files - 4 (new adapter, Program.cs, new test files)

## Recommended Workflow

### Step 1 - Metadata and detection

Where: src/HarnessGateway.Launcher.Core/Adapters/ClaudeCodeAdapter.cs

- Follow the existing adapter shape; internal detect-command delegate for tests.

Verify: metadata values match the record set exactly.

### Step 2 - Plan env-only actions

Where: same adapter file

- Emit base-url always; token action only with a saved key; model action only with a chosen model. No `ConfigFileWriteAction`.

Verify: plan contains no file-write action; `--model` appears in the CLI argument action only when requested.

### Step 3 - Register and test

Where: src/HarnessGateway.Launcher/Program.cs, src/HarnessGateway.Launcher.Tests/Adapters/ClaudeCodeAdapterTests.cs (new)

- Registration plus contract tests; restore-on-exit is trivially satisfied since no config file is written - assert that too (no backup files created).

Verify: tests pass.

## Context pointers

##### Files

- src/HarnessGateway.Launcher.Core/Adapters/IHarnessAdapter.cs - interface
- src/HarnessGateway.Launcher/Program.cs - registration
- src/HarnessGateway.Launcher/Commands/LaunchOrchestrator.cs - plan executor

##### ADRs

- Decision ledger only.

##### Domain terms

- Adapter, Harness, Configuration action, Backend.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T026 - adapter spec (env-only, token variable choice, disabled behaviour note)
- DECISIONS-LmProxy-agent-launcher.md#T019, T020, T023, T005 - metadata and closed action family
- DECISIONS-LmProxy-agent-launcher.md#D011, D012, D013, D007 - no durable writes, keys, model optional

## Acceptance criteria

- [ ] Planned actions are environment variables and an optional `--model` argument only; zero config-file writes
- [ ] `ANTHROPIC_AUTH_TOKEN` is used, not `ANTHROPIC_API_KEY`, and the choice is locked by a test
- [ ] Install hint documents the disabled MCP (Model Context Protocol) tool search and Remote Control when using a non-first-party base URL
- [ ] Adapter is registered and orchestrator tests drive a claude-code launch with a fake detection delegate

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - adapters/001-harness-metadata-supported-protocols.md, adapters/003-codex-adapter.md (Program.cs same-file ordering).
