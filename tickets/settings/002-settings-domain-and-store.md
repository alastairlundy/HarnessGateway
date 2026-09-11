---
title: Settings domain and store
classification: Independent
blocked_by: [001-solution-and-project-scaffolding]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Define the persisted settings model (protocol, backend, settings record) and its JSON store with environment-variable layering, so every launch path resolves the saved backend the same way.

## What to build

- `Backends/ProtocolKind.cs` - plain enum with `OpenAiChat`, `OpenAiResponses`, `Anthropic`, mapping one-to-one to `--protocol` values `openai-chat`, `openai-responses`, `anthropic`. Per-protocol HTTP behavior does not live here.
- `Backends/Backend.cs` - sealed record `(string Name, Uri BaseUri, ProtocolKind Protocol, string? DefaultModel, string? ApiKey)`. ApiKey is plaintext JSON in version 1; keychain/DPAPI (Data Protection API) hardening is deferred.
- `Settings/LauncherSettings.cs` - record `(List<Backend> Backends, string DefaultBackend, int SchemaVersion)`. Version 1 requires exactly one entry; the list-plus-pointer shape must tolerate hand-added multiple entries without behavior change (version 1 reads only the default pointer).
- `Settings/SettingsStore.cs` - JSON file in the platform config directory, loaded through Microsoft.Extensions.Configuration with JSON plus environment-variable layering (environment variables override file values). The store exposes load and save; callers enforce the writer scope - settings writes originate only from `config` subcommands or explicit `--persist model` on non-interactive launches, never from the wizard.
- Tests: settings round-trip; environment-variable-over-file precedence; single-entry validation for version 1.

## Size

- **Files** - 6 (4 source, 2 test)

## Recommended Workflow

### Step 1 - Define ProtocolKind and Backend

Where: `src/LmProxy.AgentLauncher.Core/Backends/`

- Create the ProtocolKind enum with the three members and the Backend sealed record.

Verify: the project compiles; the protocol-to-flag-value mapping is unit-checked.

### Step 2 - Define LauncherSettings

Where: `src/LmProxy.AgentLauncher.Core/Settings/LauncherSettings.cs`

- Create the settings record with the backends list, default backend pointer, and schema version.

Verify: the project compiles.

### Step 3 - Implement SettingsStore

Where: `src/LmProxy.AgentLauncher.Core/Settings/SettingsStore.cs`

- Resolve the platform config directory per operating system.
- Load through Microsoft.Extensions.Configuration with JSON plus environment-variable layering.
- Implement save-to-JSON.

Verify: a round-trip test passes against a temp directory.

### Step 4 - Write the settings tests

Where: `src/LmProxy.AgentLauncher.Tests/Settings/`

- Round-trip test; environment-variable-over-file precedence test; version-1 single-entry validation test.

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher.Core/` - target project for all four source files
- `src/LmProxy.AgentLauncher/LmProxy.AgentLauncher.csproj` - package availability comes from the scaffolding ticket

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Backend - an API endpoint identified by a base URL and a protocol
- Default endpoint - the user-saved backend used when no per-launch override is given

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D006` - backend protocol family (OpenAI-compatible or Anthropic-compatible)
- `DECISIONS-LmProxy-agent-launcher.md#D008` - a backend is a base URL plus one of three protocols
- `DECISIONS-LmProxy-agent-launcher.md#D009` - user-configurable default endpoint with per-launch override
- `DECISIONS-LmProxy-agent-launcher.md#D012` - ApiKey plaintext in version 1; hardening deferred
- `DECISIONS-LmProxy-agent-launcher.md#D021` - protocol wording; `--protocol` values
- `DECISIONS-LmProxy-agent-launcher.md#T006` - JSON settings in the platform config directory; environment-variable overrides
- `DECISIONS-LmProxy-agent-launcher.md#T011` - backends list + default pointer schema; version 1 requires exactly one entry
- `DECISIONS-LmProxy-agent-launcher.md#T017` - ProtocolKind enum shape
- `DECISIONS-LmProxy-agent-launcher.md#T018` - Backend record shape
- `DECISIONS-LmProxy-agent-launcher.md#T021` - LauncherSettings record shape

## Acceptance criteria

- [ ] ProtocolKind has exactly OpenAiChat, OpenAiResponses, Anthropic, mapping to `openai-chat`, `openai-responses`, `anthropic`
- [ ] Backend is a flat record with Name, BaseUri, Protocol, optional DefaultModel, optional ApiKey
- [ ] LauncherSettings stores a backends list, a default backend pointer by name, and an integer schema version; version 1 requires exactly one entry and tolerates multiple without behavior change
- [ ] Settings persist as JSON in the platform config directory; environment variables override file values
- [ ] Round-trip and precedence tests pass

## Dependencies

**Blocked by** - 001-solution-and-project-scaffolding
