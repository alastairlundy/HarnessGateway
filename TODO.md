# TODO — HarnessGateway.Launcher

Deferred decisions and future work. Standing "we deliberately don't do this" refusals live in `docs/agents/out-of-scope.md`.

## Adapters (future versions)

- [ ] OpenClaw adapter
- [ ] Hermes adapter
- [ ] Cline adapter
- [ ] Claude Desktop app support — investigate viability
- [ ] Codex Desktop app support — investigate viability

Deferred out of the v1 scope; the pattern to follow is any shipped adapter (`Core/Adapters/*Adapter.cs`).

## Effort / reasoning level passing

- [ ] Add an effort/reasoning-level knob to `launch` and thread it through each adapter:
  - Claude Code: `--effort <level>` (global; interactive + one-shot)
  - Codex: `-c model_reasoning_effort=<level>` (global; installed CLI has no dedicated flag)
  - GitHub Copilot CLI: `--reasoning-effort <level>` (global)
  - Pi: `--thinking <level>` (global; also supports `provider/model:level` shorthand)
  - Kilo: `--variant <name>` on `kilo run` only; no interactive launch flag (in-app `/variant`)
  - OpenCode: `#variant` on the model selector for runs/sessions/agents/commands; root/default model does not retain a variant
  - OpenCode Desktop: no launch-time knob (GUI/config-driven)

## Install discovery

- [ ] Portable install locations (AppImage, an unzipped archive, a folder outside the conventional roots). Discovery stays deliberately shallow, because an executable must sit where a real install puts it, directly under a root. Widen it when a portable-install case exists.

## Testing

- [ ] Real-harness integration test matrix (agent-optional integration suite) — fakes-only until adapter contract drift or release-quality concerns justify it

## Packaging and CLI identity

- [ ] Unified-CLI rename when proxy-server verbs are added (rename trigger)
