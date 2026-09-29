# Out of scope — standing refusals

Proposed, considered, and deliberately rejected behaviors for the `harnessgateway` launcher. Do not re-propose any of these without a new decision — each was a considered rejection, not an oversight. (The dated decisions behind them lived in a decision ledger that has since been deleted; this file preserves the refusals, not the process.)

## Credentials

- **Per-launch API-key overrides** (`--key` on `launch`, a `HARNESSGATEWAY_API_KEY` env var, env-var passthrough). Keys come only from the resolved backend entry's credential-store record. Rejected: argv/shell-history and process-listing secret exposure, masked config mistakes, and an undocumented contract surface. To use a different key, save it on a backend entry via `config backend add`.
- **Dummy/placeholder API keys.** When no key is saved the launcher omits the credential rather than inventing a placeholder; injection happens only when a key was saved.

## Settings

- **Settings-tracked persistence** (remembering which writes to keep in the settings file instead of marking the backups). Persistence uses backup markers that self-heal skips for `--persist config` writes; settings-tracked state was rejected for atomicity, locality, and settings-independence.
- **Hand-edited settings files as a supported workflow.** Backend entries are managed by `config backend add/switch/list/remove`, which also route keys into the platform credential store instead of plaintext JSON; a hand edit that dangles the `defaultBackend` pointer is an error pointing at `config backend switch`.
- **Auto-promotion or first-entry fallback when the default pointer dangles.** Launches read the default pointer strictly — no inference, no silent fallback.

## Launch

- **Protocol probing.** The backend protocol is never guessed from a probe; raw-URL overrides require `--endpoint` and `--protocol` together so failure classes stay deterministic and scriptable.
- **Launcher-owned environment mutation** for harness configuration (snapshotting/restoring the launcher process's own environment). Harness env-var configuration rides the spawned process plan only; nothing in the launcher process is ever mutated, so there is nothing to restore.

## Adapters

- **Derived or adoptable Codex provider ids.** The launcher writes exactly `[model_providers.harnessgateway]`. Per-backend derived ids accumulate launcher blocks in user config and complicate restore; "adopting" (overwriting) a user-authored provider block is precisely the lasting damage the launcher exists to avoid. A pre-existing block with the reserved id refuses the launch before any write.

## CLI surface

- **More short flags beyond `-m`.** `-m` is the sole alias because model input is the highest-frequency repeat input on the launch hot path; the full core alias set (`-e`, `-b`, `-p`, …) was speculative weight.
- **Additional `--persist` value types** beyond `config` and `model`. The family is deliberately closed rather than left open; any future expansion is a new decision, not a silent enum extension.
- **Backend-selection persistence.** A `--backend` launch persists nothing about its selection; changing the default backend is `config backend switch`'s job, keeping one verb per settings mutation.

## Wizard

- **A settings-writing wizard.** The wizard never writes settings — it bootstraps a working launch for the current invocation only. Defaults are saved exclusively through `config` verbs or explicit `--persist` flags on non-interactive launches.

## Testing

- **xUnit/NUnit.** Tests are TUnit (native Microsoft Testing.Platform), chosen deliberately over an xUnit v3 recommendation for its runner features.
- **Real-harness integration tests in the main gate.** Fakes-only by design to avoid networked-agent flakiness; the integration matrix stays a separate deferred suite until adapter contract drift justifies it.

## Packaging

- **NuGet/dotnet-tool distribution.** Releases ship per-OS `harnessgateway-<rid>` archives only; the tool-package channel was dropped.
- **A separately published adapters package.** Adapters stay inside the Core project until community-authored harnesses arrive; the descriptor-based adapter option remains open for that future.
