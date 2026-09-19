# TODO — HarnessGateway.Launcher

Deferred decisions and future work, each citing the Decision Ledger (`docs/decisions/DECISIONS-LmProxy-agent-launcher.md#<id>`). Backed by `IMPLEMENTATION-HarnessLauncher.md` §6.

## Adapters (future versions)

- [ ] Codex adapter [`DECISIONS-LmProxy-agent-launcher.md#D007`]
- [ ] Claude Code adapter [`#D007`]
- [ ] OpenClaw adapter [`#D007`]
- [ ] Hermes adapter [`#D007`]
- [ ] Cline adapter [`#D007`]
- [ ] Confirm warn-and-launch behavior for harnesses without a model-requirement declaration in the adapter spec [`#D014`]

## CLI surface

- [ ] `--backend <name>` resolution for saved backend entries (joins flag contract) [`#T010`]
- [ ] Short flag aliases (`-e`, `-f`, `-m`, …) if ever wanted [`#T010`]
- [ ] Backend profiles: `add` / `switch` / `list` verbs (schema already supports a list + default pointer) [`#T011`]
- [ ] Additional `--persist` value types, if real second targets appear beyond `config` and `model` — parameterized form already in place [`#T015`]
- [ ] Document the exit-code table as a stable contract across release channels [`#T012`]

## Settings

- [ ] Settings schema migration path keyed on `SchemaVersion` [`#T021`]
- [ ] Profile-aware handling when multiple backend entries exist (v1 reads only the default pointer) [`#T011`]

## Security

- [ ] Credential storage hardening: keychain/DPAPI instead of plaintext `ApiKey` in settings JSON (split-credentials record shape anticipated) [`#D012`], [`#T018`]

## Testing

- [ ] Real-harness integration test matrix (agent-optional integration suite) [`#T009`]

## Packaging and CLI identity

- [ ] Resolve root-command name (working name `harnessgateway`; `lmproxy` is taken; candidates discussed: `lmp`, `lmcli`) [`#D017`]
- [ ] Unified-CLI rename when proxy-server verbs are added (rename trigger) [`#D017`]
- [ ] Build and verify two artifact kinds per release in CI (NuGet tool + per-OS binaries) [`#T003`]

## Ecosystem (considered, deliberately not built)

- [ ] Known-backends registry/tree (product names prefilling URL + protocol) layered over the protocol-endpoint model [`#D008`]
- [ ] JSON-descriptor (data-driven) harness adapters for community contributions [`#T005`], [`#T002`]
- [ ] Publish adapters as a separate NuGet package when community harnesses arrive [`#T002`]

## Session housekeeping

- [ ] Delete `docs/decisions/DECISIONS-LmProxy-agent-launcher.md` and retire `IMPLEMENTATION-HarnessLauncher.md` once implementation is complete (persisted-by-default decision artifacts)
