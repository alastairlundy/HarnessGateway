---
title: TOML round-trip config editor
classification: Collaborative
blocked_by: []
parent: IMPLEMENTATION-TODO.md
---

## Goal

Provide a structural TOML editing capability in the Core project that lets callers insert or update blocks and top-level keys in a TOML document while preserving its comments and overall structure. It is the prerequisite for the Codex adapter's backed-up config write.

## What to build

- A Core-located type (for example `src/HarnessGateway.Launcher.Core/Support/` or a `Toml/` folder; place it inside the Core project per T002, not the harness-adapter code) that can
  - read a TOML document from a string and keep enough structure (comments, key order, block boundaries) to re-serialise it faithfully,
  - insert or replace a named block with a set of key/value pairs, and set top-level string/scalar keys,
  - detect whether a named block exists (needed for the Codex collision check in TK003).
- Semantics live in `DECISIONS-LmProxy-agent-launcher.md#T025`'s constraint line only - the mechanism is deliberately left open. The owner decision this ticket needs before implementation starts: hand-rolled structural editor (no new dependency, complete control over trivia, more code and edge cases) versus the Tomlyn package (CPM entry in `src/Directory.Packages.props`, mature parsing, comment preservation support). Do not add the dependency without the CPM entry and the user's decision - never add `Version=` on a `PackageReference`.

## Size

- Files - 2 or more (editor type, tests; plus `src/Directory.Packages.props` + project file if the package route is chosen)
- Large Files to be created - apply if a hand-rolled editor grows past 500 lines; expected to be near the threshold

## Recommended Workflow

### Step 1 - Confirm the mechanism choice

Where: N/A (owner decision)

- Present hand-rolled versus Tomlyn to the repo owner; record the choice and the CPM implications before coding.

Verify: agreed mechanism documented in the ticket or a follow-up note.

### Step 2 - Implement the editor surface

Where: Core project, new file(s)

- Read document, expose block-existence, block upsert, top-level key set, and a serialise-back operation.

Verify: a spliced-in block round-trips with comments and unrelated sections byte-for-byte equal.

### Step 3 - Characterise with fake-backed tests

Where: src/HarnessGateway.Launcher.Tests/

- Round-trip preservation, empty and comment-only files, existing-section splice, nonexistent-section insert, and an unparseable-input refusal path.

Verify: all round-trip assertions hold on provided fixtures.

## Context pointers

##### Files

- src/Directory.Packages.props - central package management; the only place a version may be declared
- src/HarnessGateway.Launcher.Core/DirectoryPackages style note - package versions are never set inline - add a `PackageVersion` entry if a dependency is chosen

##### ADRs

- Decision ledger only; `docs/adr/` does not exist in this repository.

##### Domain terms

- Configuration action - the editor implements the file-write channel.

##### Ledger records

- DECISIONS-LmProxy-agent-launcher.md#T025 - TOML edits must preserve comments and structure (round-trip); the constraint this ticket satisfies
- DECISIONS-LmProxy-agent-launcher.md#T009 - TUnit with fake-based tests; the test style follows it

## Acceptance criteria

- [ ] Editor round-trips a representative Codex `config.toml` with comments, whitespace and section order preserved
- [ ] Block insert/replace and top-level-key edit behaviours are covered by tests, including the block-exists probe
- [ ] The mechanism choice is recorded before implementation continues
- [ ] Zero or one new dependency, routed exclusively through CPM per repo convention

## Dependencies

All dependencies are tracked via the `blocked_by` field.

**Blocked by** - None - can start immediately.

## Resolution notes

- **Mechanism (Step 1 owner decision, 2026-09-26): Tomlyn.** The repo owner chose the Tomlyn package over a hand-rolled structural editor. CPM implications honoured: a single `<PackageVersion Include="Tomlyn" Version="2.10.1" />` entry in `src/Directory.Packages.props` plus a versionless `<PackageReference Include="Tomlyn" />` in `src/HarnessGateway.Launcher.Core/HarnessGateway.Launcher.Core.csproj` - no `Version=` on the PackageReference. Implementation: `src/HarnessGateway.Launcher.Core/Toml/TomlDocumentEditor.cs`, built on Tomlyn's full-fidelity `SyntaxParser` tree (`DocumentSyntax`), whose `ToString()` reproduces the source text, so untouched sections round-trip byte-for-byte and edits become a minimal diff.
