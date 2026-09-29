# HarnessGateway

## Installation

### Per-OS binaries

Download the archive for your platform from [GitHub Releases](https://github.com/alastairlundy/HarnessGateway/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `harnessgateway-linux-x64.tar.gz` |
| Windows x64 | `harnessgateway-win-x64.zip` |
| macOS x64 | `harnessgateway-osx-x64.tar.gz` |
| macOS ARM64 | `harnessgateway-osx-arm64.tar.gz` |

Extract the archive and add the directory to your `PATH`.

## Usage

```bash
harnessgateway launch [<agent>] [--backend NAME] [--endpoint URL --protocol PROTOCOL] [--model MODEL | -m MODEL] [--persist config|model]
```

`-m` is the short alias for `--model`; no other short flags are defined.

Running `harnessgateway` with no arguments, or `harnessgateway launch` with no agent and no options, starts the interactive wizard (harness → endpoint → key → model). The wizard never writes settings. Launch options without an agent are still a parse error, so no flag you typed is silently dropped.

`--backend <name>` selects a saved backend entry to launch against. `--endpoint`, `--protocol`, and `--model` then layer on top of the resolved entry field by field: each flag replaces only its own field, and every other field - including the entry's stored API key - comes from the saved entry. Keys are not overridable per launch; the resolved entry's stored key is the only credential source.

Without `--backend`, launch resolves the entry named by the `defaultBackend` pointer and nothing else: no first-entry fallback and no auto-promotion. A `--backend` name or default pointer that resolves no saved entry fails with exit code 8 and the message `unknown backend name '<name>'`; an unresolvable default pointer directs you to `harnessgateway config backend switch`. Best-effort "did you mean" hints may accompany the message but are non-contractual.

`--persist` accepts exactly `config` and `model`. A `--backend` launch persists nothing about its selection - changing the default backend remains `config backend switch`'s job.

### config backend

```bash
harnessgateway config backend add <name> --endpoint URL --protocol PROTOCOL [--key SECRET]
harnessgateway config backend switch <name>
harnessgateway config backend list
harnessgateway config backend remove <name>
```

`add` creates the named entry or replaces it when the name already exists. `--endpoint` and `--protocol` are both required on every add and travel together (the same pairing rule as launch overrides); a missing or one-sided pair, a bad URL, or an unknown protocol word exits 2. `add` never moves the default pointer - after a first `add`, run `config backend switch <name>` to make it the default (the success output for a new entry hints at this).

Key input on `add` follows one contract: with no `--key`, an interactive terminal is asked first (`Set an API key for '<name>'? [y/N]`, worded `Replace` when the entry already has a key) and the key itself is then read with hidden input; declining the question, pressing Enter on an empty prompt, or running with redirected stdin yields a keyless entry, which is valid. `--key <secret>` skips the prompt for scripts, but the secret transits your shell history - prefer the interactive prompt outside of automation. Keys are stored only in the platform credential store; the settings JSON records just an `hgkey:<name>` reference, never the secret. A credential-store failure exits 7 and writes nothing.

`switch` validates that the named entry exists, writes the `defaultBackend` pointer, and prints `backend '<name>' is now the default`. It never modifies the entry itself, and it is idempotent: switching to the current default succeeds with the same confirmation without rewriting the settings file.

`list` is read-only and marks the default entry with `*`.

`remove` deletes the entry and any credential stored for it (a store failure exits 7 and removes nothing). It refuses with exit 2 to remove the current default backend - `switch` to another entry first - and refuses to remove the last remaining backend, so settings never become backend-less.

On `switch` and `remove`, a name that resolves no saved entry exits 8 with `unknown backend name '<name>'`; best-effort "did you mean" hints may accompany the message but are non-contractual.

## License

This project is licensed under the [Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/).

## Exit codes

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded |
| 2 | UsageError | Invalid arguments or missing required options |
| 3 | UnknownAgent | Unknown agent / invalid agent-backend selection: requested agent harness is not recognised, or the selected backend's protocol is not supported by that harness |
| 4 | HarnessUnavailable | Agent harness binary not found on PATH |
| 5 | EndpointUnreachable | Backend endpoint could not be reached |
| 6 | RequiredModelMissing | No model was specified and no default is configured |
| 7 | ConfigRestoreFailure | Failed to restore configuration after a no-persist launch |
| 8 | UnknownBackendName | Unresolvable backend-name reference: `--backend` names no saved entry, or the `defaultBackend` pointer dangles or is empty while entries exist. A named reference carries the message `unknown backend name '<name>'` |
| 9 | UnsupportedSettingsSchema | Settings file declares an unsupported settings schema version - the field is missing, malformed, unknown-older, or newer than this build; the file is left untouched and nothing is written |
| 10 | ConfigConflict | Launch was refused because existing configuration conflicts with the launcher's writes (for example a pre-existing provider block). Resolve the conflict and launch again |

Settings load migrates older schema versions forward automatically: a version-1 file's plaintext API keys are rewritten as credential-store references. Migration is forward-only and irreversible - a migrated file can no longer be usefully read by an older build (accepted per T039). A file whose `schemaVersion` is missing, malformed, or newer than this build is refused with exit 9 and left untouched.
