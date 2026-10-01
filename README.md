# HarnessGateway

Points an agent CLI at an OpenAI- or Anthropic-compatible endpoint and starts it.
Credentials live in your OS keychain, never in the settings file.

## Install

Grab the archive for your platform from [GitHub Releases](https://github.com/alastairlundy/HarnessGateway/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `harnessgateway-linux-x64.tar.gz` |
| Linux ARM64 | `harnessgateway-linux-arm64.tar.gz` |
| Windows x64 | `harnessgateway-win-x64.zip` |
| Windows ARM64 | `harnessgateway-win-arm64.zip` |
| macOS x64 | `harnessgateway-osx-x64.tar.gz` |
| macOS ARM64 | `harnessgateway-osx-arm64.tar.gz` |

Unpack it anywhere and put the directory on your `PATH`. The binaries are
self-contained, so there's no .NET runtime to install.

Linux also ships native packages, one per Linux RID:

| Distro | Package |
|--------|---------|
| Debian / Ubuntu | `harnessgateway-linux-x64.deb`, `harnessgateway-linux-arm64.deb` |
| Fedora / RHEL | `harnessgateway-linux-x64.rpm`, `harnessgateway-linux-arm64.rpm` |

The `.deb` and `.rpm` packages are shipped **unsigned** — no GPG key material
exists in this pipeline, so there is no signature to import and no signed
repository to add. Install them straight from the downloaded file with
`dpkg -i harnessgateway-linux-x64.deb` or
`rpm -i harnessgateway-linux-x64.rpm` (or `dnf install ./<package>`) and
expect your package manager to warn about the missing signature; download only
from the GitHub Releases page above. The archives are also plain binaries —
unpacking one and adding it to `PATH` works the same way on every platform.

## Harnesses

HarnessGateway supports six agent harnesses. None of them ship with it: install
the ones you want from the table below, and the launcher will find them on your
`PATH`.

| Token | Name | Binary | Protocols | Model | Install |
|-------|------|--------|-----------|-------|---------|
| `opencode` | OpenCode | `opencode` | chat, responses, anthropic | required | `npm i -g @opencode/cli` |
| `kilo` | Kilo | `kilo` | chat, responses, anthropic | required | `npm i -g @kilocode/cli` |
| `copilot` | GitHub Copilot CLI | `copilot` | chat, responses, anthropic | required | `npm i -g @github/copilot` |
| `pi` | Pi | `pi` | chat, responses, anthropic | optional | `npm i -g @earendil-works/pi-coding-agent` |
| `codex` | codex | `codex` | responses | required | `npm i -g @openai/codex` |
| `claude-code` | claude-code | `claude` | anthropic | optional | `npm i -g @anthropic-ai/claude-code` |

"chat" is `openai-chat`, "responses" is `openai-responses`. Asking for a
protocol a harness doesn't speak exits 3 and names the ones it does.

Type the **Token** column: `harnessgateway launch copilot`. The **Name** column
is what the interactive wizard lists and what error messages quote, and it is
not accepted as input. For five of the six the two happen to be the same
string, which is why the difference is easy to miss until you reach Copilot.
Matching is case-insensitive, so `launch COPILOT` works too. A few extra
spellings are accepted as aliases (`claude` for `claude-code`,
`github-copilot-cli` for `copilot`), and `--help` lists only the tokens.

Detection is a `PATH` lookup, so a harness that is installed but not on `PATH`
reads as missing. Naming the binary is how you check:

```bash
opencode --version
copilot --version
```

A named harness that isn't found exits 4 and prints its install line. The
wizard annotates harnesses it can't detect `not installed` and refuses them at
selection with the same hint.

HarnessGateway never edits your shell profile or its own environment. Every
setting below is scoped to the process it starts:

| Token | How it's configured |
|-------|---------------------|
| `opencode`, `kilo`, `pi` | `OPENAI_*` or `ANTHROPIC_*` env vars |
| `copilot` | `COPILOT_PROVIDER_BASE_URL`, `COPILOT_PROVIDER_API_KEY`, `COPILOT_PROVIDER_TYPE` |
| `claude-code` | `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_MODEL` |
| `codex` | a `model_providers.harnessgateway` block written to `config.toml` |

## Usage

```bash
harnessgateway launch [AGENT] [options]
```

| Option | Effect |
|--------|--------|
| `--backend NAME` | Launch against a saved backend entry instead of the default one |
| `--endpoint URL --protocol PROTOCOL` | Override both fields of the resolved entry |
| `--model MODEL`, `-m MODEL` | Override the model for this launch only |
| `--persist config` | Keep the codex `config.toml` edit instead of restoring the original on exit |
| `--persist model` | Save the resolved model as the entry's default for later launches |

`--endpoint` and `--protocol` only make sense together; either alone is a
parse error. `--persist` takes both values at once (`--persist config model`) or
comma-separated (`--persist config,model`).

```bash
harnessgateway --version
```

`--version` prints the assembly informational version (the release pin, e.g.
`0.1.0`) and exits 0. It is how you identify your build when reporting a bug —
the version deliberately stays out of archive file names, and it is unrelated
to the settings `schemaVersion` reported by `config get schemaVersion`.

Running `harnessgateway` with no arguments, or `harnessgateway launch` with no
agent and no options, starts a wizard that asks for harness, endpoint, key, and
model in that order. It fetches the endpoint's model list to populate the last
step, and falls back to free text when that request fails. The wizard saves
nothing. Give it options with no agent and you'll get a parse error rather than
a silently ignored flag.

### Choosing a backend

`--backend NAME` picks a saved entry. `--endpoint`, `--protocol`, and `--model`
then layer on top of it one field at a time; everything else, including the
saved key, comes from the entry. Keys are never per-launch.

Without `--backend`, launch resolves the `defaultBackend` pointer and nothing
else. There's no first-entry fallback and no auto-promotion, so a settings file
with entries but no default exits 8 until you run
`config set defaultBackend <name>`. A name that matches no entry exits 8 with
`unknown backend name '<name>'`, plus a suggestion when one is close.

### What `--persist config` actually does

Most harnesses are configured through environment variables and leave no trace
on disk. `codex` is the exception. It reads its backend from `config.toml`, so
HarnessGateway writes a `model_providers.harnessgateway` block there
(`$CODEX_HOME/config.toml`, or `~/.codex/config.toml`) and passes the key
through the environment variable that block names.

By default the launcher copies your `config.toml` to a backup directory before
writing, then restores the original as soon as the `codex` process exits. Run
`harnessgateway launch codex` once with no `--persist config` and your file
comes back byte-identical every time, which is usually what you want.

Pass `--persist config` to skip the restore and keep the edited file. A launch
that dies before restoring leaves its backup behind, and the next launch replays
it before doing anything else, so a crash can't leave you with a half-written
config.

If `config.toml` already has a `model_providers.harnessgateway` block that you
wrote yourself, the launch is refused with exit 10. The launcher won't adopt or
overwrite it. Rename the block or point `CODEX_HOME` elsewhere.

`--persist model` writes the resolved model into the resolved entry so later
launches reuse it. A `--backend` launch never changes which entry is the
default; only `config set defaultBackend` does.

## Configuration

```bash
harnessgateway config show
harnessgateway config get <key>
harnessgateway config set <key> <value>
```

`show` prints the resolved default backend — endpoint, protocol, default model,
and whether a key is set (never the key itself). With no default it says so and
prints the command that fixes it.

### Typed keys

`get` and `set` address a closed set of typed keys over the settings file — no
raw JSON paths:

| Key | Access | Value |
|-----|--------|-------|
| `defaultBackend` | read/write | Name of the default backend entry — what `launch` resolves when no `--backend` is given |
| `schemaVersion` | read-only | Settings-file schema version, stamped by the loader |
| `backends` | read-only | Saved backend names, one per line — never endpoints, protocols, or keys |
| `backends.<name>.endpoint` | read/write | Absolute URL of a saved entry |
| `backends.<name>.protocol` | read/write | `openai-chat`, `openai-responses`, or `anthropic` |
| `backends.<name>.defaultModel` | read/write | Per-entry default model |

Anything outside that set is a usage error: an unknown key, a key naming no
saved entry, or a write to `schemaVersion` or `backends` all exit 2. Backend
creation and deletion are not key-addressable — `backend add` and
`backend remove` own those, and no `set` key can write secret material.

`config set defaultBackend <name>` is the sole write path for the default
pointer: it validates the name against the saved entries, prints
`Default backend set to '<name>'`, succeeds idempotently when the pointer
already names that entry, and exits 8 with `unknown backend name '<name>'` (plus
a suggestion when one is close) when nothing matches.

### Backends

```bash
harnessgateway config backend add <name> --endpoint URL --protocol PROTOCOL [--key SECRET]
harnessgateway config backend list
harnessgateway config backend remove <name>
harnessgateway config backend set-key <name> [--key SECRET]
harnessgateway config backend clear-key <name>
```

`add` creates the entry or replaces an existing one of the same name. Both
`--endpoint` and `--protocol` are required, and a bad URL or unknown protocol
word exits 2. It does not move the default pointer, so run
`config set defaultBackend <name>` after your first `add`; the success message
reminds you.

Without `--key`, an interactive terminal gets `Set an API key for '<name>'? [y/N]`
(worded `Replace` if the entry already has one) followed by a hidden prompt.
Answering no, pressing Enter on an empty prompt, or piping stdin all produce a
keyless entry, which is valid. `--key` skips the prompt for scripts, at the cost
of the secret landing in your shell history. A credential-store failure exits 7
and writes nothing.

`list` marks the default with `*` and prints an add-an-entry hint when the file
is empty, or a `set defaultBackend` hint when entries exist but no default is
set.

`remove` deletes the entry and its stored key. It refuses with exit 2 to remove
the current default (set a new default first) or the last remaining entry.

`set-key` sets or replaces the saved API key for one backend. It touches only
the credential store — the entry's endpoint, protocol, and default model are
never changed. It follows the same prompt rules as `add` (`--key` skips the
hidden prompt), reports success without writing when the prompt is declined, and
exits 7 writing nothing on a credential-store failure.

`clear-key` removes the saved key and leaves everything else about the entry
alone. It is idempotent: clearing a keyless backend succeeds and says there was
nothing to clear. A credential-store failure exits 7 and writes nothing.

An unknown name on `remove`, `set-key`, or `clear-key` exits 8 — as does a
`config set defaultBackend` write that names no entry.

## Where things are stored

| | |
|---|---|
| Windows | `%APPDATA%\HarnessGateway` |
| macOS | `~/Library/Application Support/HarnessGateway` |
| Linux | `$XDG_CONFIG_HOME/HarnessGateway`, else `~/.config/HarnessGateway` |

`HarnessGateway-launcher.json` holds the entry list, the default pointer, and
per-entry default models. A key appears there only as `hgkey:<name>`; the secret
is in DPAPI, the macOS keychain, or Linux secret-service under that identifier.

Loading a version-1 file migrates it forward by moving plaintext keys into the
credential store and rewriting them as references. That migration is one-way:
once it runs, an older build can no longer read the file usefully. A file whose
`schemaVersion` is missing, malformed, or newer than this build is refused with
exit 9 and left untouched.

## Exit codes

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded |
| 2 | UsageError | Invalid arguments, a launch with no backend configured, or a `remove` that would break the default pointer or empty the entry list |
| 3 | UnknownAgent | Unrecognized agent token, or a protocol the chosen harness doesn't speak. A near miss suggests the token it meant |
| 4 | HarnessUnavailable | The harness binary isn't on `PATH` |
| 5 | EndpointUnreachable | **Reserved.** Declared but never returned; nothing currently probes an endpoint's reachability |
| 6 | RequiredModelMissing | **Reserved.** Declared but never returned; a harness that needs a model and has none is warned about, then launched anyway |
| 7 | ConfigRestoreFailure | Credential-store failure, or an unhandled launch error. Messages from the unhandled path start with `Launch failed:` |
| 8 | UnknownBackendName | `--backend` named no entry, or the `defaultBackend` pointer dangles or is empty while entries exist |
| 9 | UnsupportedSettingsSchema | `schemaVersion` is missing, malformed, or newer than this build. Nothing is written |
| 10 | ConfigConflict | A user-authored `model_providers.harnessgateway` block already exists in the codex config |

Codes 5 and 6 are reserved and will not appear in this release. The enum keeps
them so a future build can claim the numbers without renumbering anything
already documented, and the numbering is append-only from here.

## License

[Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/).
