# TODO — HarnessGateway.Launcher

Deferred decisions and future work. Standing "we deliberately don't do this" refusals live in `docs/agents/out-of-scope.md`.

## Adapters (future versions)

- [ ] OpenClaw adapter
- [ ] Hermes adapter
- [ ] Cline adapter

Deferred out of the v1 scope; the pattern to follow is any shipped adapter (`Core/Adapters/*Adapter.cs`).

## CLI surface

- [ ] Short flag aliases beyond the single shipped `-m` (`-e`, `-b`, `-p`, …) only when typing friction justifies them

## Testing

- [ ] Real-harness integration test matrix (agent-optional integration suite) — fakes-only until adapter contract drift or release-quality concerns justify it

## Packaging and CLI identity

- [ ] Unified-CLI rename when proxy-server verbs are added (rename trigger)
