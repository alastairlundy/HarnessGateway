# TODO — HarnessGateway.Launcher

Deferred decisions and future work. Standing "we deliberately don't do this" refusals live in `docs/agents/out-of-scope.md`.

## Adapters (future versions)

- [ ] OpenClaw adapter
- [ ] Hermes adapter
- [ ] Cline adapter
- [ ] Claude Desktop app support — investigate viability
- [ ] Codex Desktop app support — investigate viability

Deferred out of the v1 scope; the pattern to follow is any shipped adapter (`Core/Adapters/*Adapter.cs`).

## CLI surface

- [ ] Short flag aliases beyond the single shipped `-m` (`-e`, `-b`, `-p`, …) only when typing friction justifies them

## Testing

- [ ] Real-harness integration test matrix (agent-optional integration suite) — fakes-only until adapter contract drift or release-quality concerns justify it

## Packaging and CLI identity

- [ ] Windows `.msi`/`.exe` installer for a future version. This is a deferral, not a refusal; v0.1.0 ships Windows as zip only. dotnet-releaser's package formats are Linux-oriented (`.deb`/`.rpm`), so the future-version decision includes re-evaluating the packaging tool.
- [ ] Unified-CLI rename when proxy-server verbs are added (rename trigger)
