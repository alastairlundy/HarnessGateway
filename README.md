# HarnessGateway

Points an agent CLI at an OpenAI- or Anthropic-compatible endpoint and starts it.
Credentials live in your OS keychain, never in the settings file.

## Install

Grab the archive for your platform from [GitHub Releases](https://github.com/alastairlundy/HarnessGateway/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `harnessgateway-linux-x64.tar.gz` |
| Windows x64 | `harnessgateway-win-x64.zip` |
| macOS x64 | `harnessgateway-osx-x64.tar.gz` |
| macOS ARM64 | `harnessgateway-osx-arm64.tar.gz` |

Unpack it anywhere and put the directory on your `PATH`. The binaries are
self-contained, so there's no .NET runtime to install.

## Harnesses

HarnessGateway supports six agent harnesses. None of them ship with it: install
the ones you want from the table below, and the launcher will find them on your
`PATH`.

| Agent | Binary | Protocols | Model | Install |
|-------|--------|-----------|-------|---------|
| `OpenCode` | `opencode` | chat, responses, anthropic | required | `npm i -g @opencode/cli` |
| `Kilo` | `kilo` | chat, responses, anthropic | required | `npm i -g @kilocode/cli` |
| `GitHub Copilot CLI` | `copilot` | chat, responses, anthropic | required | `npm i -g @github/copilot` |
| `Pi` | `pi` | chat, responses, anthropic | optional | `npm i -g @earendil-works/pi-coding-agent` |
| `codex` | `codex` | responses | required | `npm i -g @openai/codex` |
| `claude-code` | `claude` | anthropic | optional | `npm i -g @anthropic-ai/claude-code` |

"chat" is `openai-chat`, "responses" is `openai-responses`. Asking for a
protocol a harness doesn't speak exits 3 and names the ones it does.

The agent argument matches the name in the table, case-insensitively and in
full, so `launch opencode` and `launch OpenCode` are the same thing.
`launch copilot` is not the same as `launch "GitHub Copilot CLI"` and exits 3.

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

| Agent | How it's configured |
|-------|---------------------|
| `OpenCode`, `Kilo`, `Pi` | `OPENAI_*` or `ANTHROPIC_*` env vars |
| `GitHub Copilot CLI` | `COPILOT_PROVIDER_BASE_URL`, `COPILOT_PROVIDER_API_KEY`, `COPILOT_PROVIDER_TYPE` |
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
with entries but no default exits 8 until you run `config backend switch`. A
name that matches no entry exits 8 with `unknown backend name '<name>'`, plus a
suggestion when one is close.

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
launches reuse it. A `--backend` launch never changes which entry is the default;
that's `config backend switch`'s job.

## Configuration

```bash
harnessgateway config show
harnessgateway config set-default-endpoint <url> <protocol>
harnessgateway config set-saved-key <key>
harnessgateway config clear-saved-key
harnessgateway config set-default-model <model>
```

These operate on the default backend, creating one named `default` if the file
is empty. `set-default-endpoint` and `set-saved-key` are the two you need on a
first run; `show` prints the resolved default and reports whether a key is set
without printing it.

### Backends

```bash
harnessgateway config backend add <name> --endpoint URL --protocol PROTOCOL [--key SECRET]
harnessgateway config backend switch <name>
harnessgateway config backend list
harnessgateway config backend remove <name>
```

`add` creates the entry or replaces an existing one of the same name. Both
`--endpoint` and `--protocol` are required, and a bad URL or unknown protocol
word exits 2. It does not move the default pointer, so run `switch` after your
first `add`; the success message reminds you.

Without `--key`, an interactive terminal gets `Set an API key for '<name>'? [y/N]`
(worded `Replace` if the entry already has one) followed by a hidden prompt.
Answering no, pressing Enter on an empty prompt, or piping stdin all produce a
keyless entry, which is valid. `--key` skips the prompt for scripts, at the cost
of the secret landing in your shell history. A credential-store failure exits 7
and writes nothing.

`switch` validates the name, writes the pointer, and prints
`backend '<name>' is now the default`. Pointing it at the current default
succeeds without rewriting the file.

`list` marks the default with `*` and prints an add-an-entry hint when the file
is empty.

`remove` deletes the entry and its stored key. It refuses with exit 2 to remove
the current default (switch first) or the last remaining entry.

An unknown name on `switch` or `remove` exits 8.

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
| 2 | UsageError | Invalid arguments, or a `remove` that would break the default pointer or empty the entry list |
| 3 | UnknownAgent | Unrecognized agent name, or a protocol the chosen harness doesn't speak |
| 4 | HarnessUnavailable | The harness binary isn't on `PATH` |
| 5 | EndpointUnreachable | Reserved; not currently returned |
| 6 | RequiredModelMissing | Reserved; a missing model currently warns and launches anyway |
| 7 | ConfigRestoreFailure | Credential-store failure, no backend configured, or an unhandled launch error |
| 8 | UnknownBackendName | `--backend` named no entry, or the `defaultBackend` pointer dangles or is empty while entries exist |
| 9 | UnsupportedSettingsSchema | `schemaVersion` is missing, malformed, or newer than this build. Nothing is written |
| 10 | ConfigConflict | A user-authored `model_providers.harnessgateway` block already exists in the codex config |

## License

[Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/).
