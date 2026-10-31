# Design: Performance scan fixes C1, I1–I5 (2026-10-31)

## Intent

Address the six findings from the analyzing-dotnet-performance scan: one sync-over-async
blocking call (C1) and five info-tier allocation/serialization cleanups. No behavior
changes beyond what the findings demand; every existing test contract (byte-for-byte
TOML, exit codes, message shape) stays intact.

## Approaches (considered & picked)

- **C1 Blocking `GetAwaiter().GetResult()` in `SettingsStore.Load`** — picked:
  convert to async. `Load` → `LoadAsync(CancellationToken)`; `SweepLegacyPlaintextKeys`
  becomes async and awaits. Alternative (keep sync facade) rejected: Linux/Mac key
  stores spawn child processes, so a sync wrapper is genuinely thread-blocking.
- **I1 Double settings parse** — picked: parse the JSON once into a `JsonDocument`,
  read `schemaVersion` from the root, then materialize settings via
  `JsonSerializer.Deserialize<JsonElement>`-free path (`root.Deserialize<LauncherSettings>`).
  Folded into the C1 rework since both rewrite `SettingsStore.Load`.
- **I2 Undisposed `HttpResponseMessage`** — picked: `using` declarations in
  `ModelCatalogClient` for both fetch methods.
- **I3 Dead `Replace` chain on hex backup path** — picked: delete the two `Replace`
  calls in `BackupService.GetBackupPath`; `Convert.ToHexString` output can never
  contain `/` or `\`.
- **I4 `BuildAppendSeparator` full re-serialize** — picked: probe the Tomlyn syntax
  tree (end-of-file leading trivia / last sibling's `EndOfLineToken`) instead of
  `_document.ToString()`. Separated into separator-focused tests first; byte-for-byte
  TOML tests (`TomlDocumentEditorTests`, `LaunchFlowTests` Codex diff asserts) are the
  regression fence.
- **I5 Reflection-based System.Text.Json** — picked: one source-generated
  `JsonSerializerContext` per JSON root: a `Core`-level context with
  `JsonSerializable` for `LauncherSettings`, `BackupRecord`, `Dictionary<string,string>`
  (key entries), and the four `ModelCatalogClient` DTOs (plus their `List<>`
  wrappers). All call sites pass `Context.Default...`; cached static options remain
  for option-carrying calls. ReadFromJsonAsync move to the context's options where
  equivalent.

## Goals

- Zero thread-blocking on async in production code.
- Settings file parsed exactly once per command.
- No response-handle leaks, no dead allocations, no full-document serialize for a
  newline probe, all JSON serialization reflection-free.

## Non-goals / anti-goals

- No exit-code, message-shape, or backup/restore semantic changes (README contract).
- No `[GeneratedRegex]`, `ArrayPool`, or other micro-opts — nothing scanned for them.
- Do not seal/add public API beyond the `LoadAsync` rename.

## Constraints

- TUnit + native MTP: `dotnet test --solution src/HarnessGateway.slnx`, TUnit filter
  is `--treenode-filter`; assertions are `await Assert.That(...)`.
- CPM: no `Version=` inline.
- `SettingsStore.Load` is used by `LaunchFlow`, `WizardPipeline`, config/backend verbs,
  and many tests — rename propagates everywhere it's called.
- AGENTS.md: file-scoped namespaces; explicit types in refactored code.

## Left to the builder

- Exact trivia-probing mechanism for I4 (builder's call, fenced by tests).
- Whether `ReadFromJsonAsync` keeps `HttpRequestHeaders`-level options or context
  options (equivalent, builder's call).
