---
title: Model catalog client
classification: Independent
blocked_by: [002-settings-domain-and-store]
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Fetch each protocol's models listing over HTTP and normalize it into the protocol-free Model catalog, degrading gracefully when the endpoint is unreachable so a launch never fails on catalog fetch alone.

## What to build

- `Backends/Models/ModelCatalog.cs` - `ModelCatalogEntry(string Id, string? DisplayName)` and `ModelCatalog(IReadOnlyList<ModelCatalogEntry> Models, DateTimeOffset FetchedAt)`.
- `Backends/Models/ModelCatalogClient.cs` - hand-rolled per-protocol HttpClient calls: OpenAI `GET /v1/models` (response shape shared by both OpenAI protocols) and Anthropic `GET /v1/models` (sends the `anthropic-version` header, and the key header when a saved key is present). JSON shapes are hand-maintained. When the endpoint is unreachable the client degrades gracefully - it returns an empty or unavailable catalog result rather than throwing - so the wizard model step can offer skip or manual entry instead of failing the launch.
- Tests: per-protocol parsing via stub HttpMessageHandler fixtures; unreachable-endpoint degradation.

## Size

- **Files** - 4 (2 source, 2 test)

## Recommended Workflow

### Step 1 - Define the catalog types

Where: `src/LmProxy.AgentLauncher.Core/Backends/Models/ModelCatalog.cs`

- Create ModelCatalogEntry and ModelCatalog records.

Verify: the project compiles.

### Step 2 - Implement the OpenAI models call

Where: `src/LmProxy.AgentLauncher.Core/Backends/Models/ModelCatalogClient.cs`

- Implement `GET /v1/models` handling shared by the OpenAiChat and OpenAiResponses protocols, normalizing into catalog entries.

Verify: a stub-handler test parses a fixture response into expected entries.

### Step 3 - Implement the Anthropic models call

Where: `src/LmProxy.AgentLauncher.Core/Backends/Models/ModelCatalogClient.cs`

- Implement `GET /v1/models` with the `anthropic-version` header and optional key header, normalizing into catalog entries.

Verify: a stub-handler test parses a fixture response into expected entries.

### Step 4 - Implement graceful degradation

Where: `src/LmProxy.AgentLauncher.Core/Backends/Models/ModelCatalogClient.cs`

- Return an unavailable/empty catalog result on unreachable endpoints or malformed responses instead of throwing.

Verify: the degradation test returns a non-throwing unavailable result.

### Step 5 - Run the test suite

Where: N/A

Verify: `dotnet test` is green.

## Context pointers

##### Files

- `src/LmProxy.AgentLauncher.Core/Backends/Models/` - target folder (created by this ticket)
- `src/LmProxy.AgentLauncher.Core/Backends/ProtocolKind.cs` - the protocol switch this client dispatches on

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Model catalog - the set of models a Backend declares as available
- Backend - the endpoint whose models listing is fetched

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D013` - the models-listing endpoint feeds the wizard model step; graceful degradation when unreachable
- `DECISIONS-LmProxy-agent-launcher.md#T007` - hand-rolled per-protocol HttpClient calls; JSON shapes hand-maintained
- `DECISIONS-LmProxy-agent-launcher.md#T022` - normalized entry pair plus fetch time

## Acceptance criteria

- [ ] ModelCatalogEntry carries Id and optional DisplayName; ModelCatalog carries the entries plus FetchedAt
- [ ] The OpenAI `/v1/models` call serves both OpenAI protocols; the Anthropic `/v1/models` call sends the version header and optional key header
- [ ] An unreachable or malformed endpoint yields a graceful unavailable result, not an exception
- [ ] Per-protocol stub-handler tests and the degradation test pass

## Dependencies

**Blocked by** - 002-settings-domain-and-store
