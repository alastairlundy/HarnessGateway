# TODO — HarnessGateway.Launcher

Deferred decisions and future work, citing the Decision Ledger (`docs/decisions/DECISIONS-LmProxy-agent-launcher.md#<id>`). Standing "we deliberately don't do this" refusals live in `docs/agents/out-of-scope.md`.

## Adapters (future versions)

- [ ] OpenClaw adapter [`DECISIONS-LmProxy-agent-launcher.md#D007`]
- [ ] Hermes adapter [`#D007`]
- [ ] Cline adapter [`#D007`]

## CLI surface

- [ ] Short flag aliases beyond the single shipped `-m` (`-e`, `-b`, `-p`, …) only when typing friction justifies them [`#T041`]

## Testing

- [ ] Real-harness integration test matrix (agent-optional integration suite) — fakes-only until adapter contract drift or release-quality concerns justify it [`#T009`], [`#T043`]

## Packaging and CLI identity

- [ ] Unified-CLI rename when proxy-server verbs are added (rename trigger) [`#D017`]

## Session housekeeping

- [ ] Before deleting `docs/decisions/DECISIONS-LmProxy-agent-launcher.md`: confirm ledger-only knowledge (rejected-alternative rationales) is preserved in `docs/agents/out-of-scope.md` and harness research facts in adapter code/README, then sweep dangling `D###`/`T###` references in this file, `AGENTS.md`, and `GLOSSARY.md`, and retire `IMPLEMENTATION-TODO.md`
