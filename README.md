# HarnessGateway

HarnessGateway is a command-line launcher for agent harnesses. It points the
harness you choose at an OpenAI- or Anthropic-compatible endpoint and starts
it. Credentials live in your OS keychain, never in the settings file.

## Install

To install HarnessGateway, download the archive for your platform from
[GitHub Releases](https://github.com/alastairlundy/HarnessGateway/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `harnessgateway-linux-x64.tar.gz` |
| Linux ARM64 | `harnessgateway-linux-arm64.tar.gz` |
| Windows x64 | `harnessgateway-win-x64.zip` |
| Windows ARM64 | `harnessgateway-win-arm64.zip` |
| macOS x64 | `harnessgateway-osx-x64.tar.gz` |
| macOS ARM64 | `harnessgateway-osx-arm64.tar.gz` |

Unpack the archive anywhere and put the directory on your `PATH`. The binaries
are self-contained, so there's no .NET runtime to install.

Linux also ships native packages, for both architectures:

| Distro | Package |
|--------|---------|
| Debian / Ubuntu | `harnessgateway-linux-x64.deb`, `harnessgateway-linux-arm64.deb` |
| Fedora / RHEL | `harnessgateway-linux-x64.rpm`, `harnessgateway-linux-arm64.rpm` |

The `.deb` and `.rpm` packages are unsigned. The build pipeline carries no GPG
key material, so there is nothing to import and no signed repository to add.
Install them straight from the downloaded file with
`dpkg -i harnessgateway-linux-x64.deb` or
`rpm -i harnessgateway-linux-x64.rpm` (`dnf install ./<package>` works too).
Expect your package manager to warn about the missing signature, so download
only from the GitHub Releases page above. The plain archives work the same way
on every platform.

## Harnesses

HarnessGateway supports six agent harnesses, none of which ship with it.
Install the ones you want from the table below, and the launcher will find
them on your `PATH`.

| Token | Name | Binary | Protocols | Model | Install |
|-------|------|--------|-----------|-------|---------|
| `opencode` | OpenCode | `opencode` | chat, responses, anthropic | required | `npm i -g @opencode/cli` |
| `kilo` | Kilo | `kilo` | chat, responses, anthropic | required | `npm i -g @kilocode/cli` |
| `copilot` | GitHub Copilot CLI | `copilot` | chat, responses, anthropic | required | `npm i -g @github/copilot` |
| `pi` | Pi | `pi` | chat, responses, anthropic | optional | `npm i -g @earendil-works/pi-coding-agent` |
| `codex` | codex | `codex` | responses | required | `npm i -g @openai/codex` |
| `claude-code` | claude-code | `claude` | anthropic | optional | `npm i -g @anthropic-ai/claude-code` |

"chat" is shorthand for `openai-chat`, and "responses" for `openai-responses`.
If you ask for a protocol a harness doesn't speak, the launcher exits with
exit code 3 and names the protocols that harness supports.

The **Token** column holds the word you type: `harnessgateway launch copilot`.
The **Name** column is what the interactive wizard lists and what error
messages quote, and the launcher does not accept it as input. For five of the
six harnesses the token and the name match, so the difference is easy to miss
until you reach Copilot. Matching is case-insensitive, so `launch COPILOT`
works too. The launcher also accepts aliases: `claude` for `claude-code`, and
`github-copilot-cli` for `copilot`. The `--help` output lists tokens only.

Detection is a `PATH` lookup, so a harness that is installed but not on your
`PATH` looks missing. Check with the binary's own name:

```bash
opencode --version
copilot --version
```

If the harness you name isn't found, the launch command exits with exit code 4
and prints the install line for that harness. The wizard marks harnesses it
cannot detect as `not installed` and prints the same hint if you pick one.

HarnessGateway never edits your shell profile or its own environment. The
launcher passes every setting below only to the process it starts:

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
| `--endpoint URL --protocol PROTOCOL` | Override the endpoint and protocol of the selected entry |
| `--model MODEL`, `-m MODEL` | Override the model for this launch only |
| `--persist config` | Keep the codex `config.toml` edit instead of restoring the original on exit |
| `--persist model` | Save this launch's model as the entry's default for later launches |

`--endpoint` and `--protocol` only make sense together; using either one alone
is a parse error. `--persist` takes both values at once
(`--persist config model`) or comma-separated (`--persist config,model`).

```bash
harnessgateway --version
```

`harnessgateway --version` prints the version number of your build, for
example `0.1.0`, and exits with exit code 0. Quote it when you report a bug,
because the version never appears in the archive file names. It is unrelated
to the settings `schemaVersion` that `config get schemaVersion` reports.

Running `harnessgateway` with no arguments, or `harnessgateway launch` with no
agent and no options, starts a wizard. The wizard asks for the harness,
endpoint, key, and model in that order, fetches the endpoint's model list to
fill in the last step, and falls back to free text if that request fails. It
saves nothing. Options without an agent are a parse error, not a silently
ignored flag.

### Choosing a backend

`--backend NAME` picks a saved entry, and the `--endpoint`, `--protocol`, and
`--model` options override its fields one at a time. Everything else,
including the saved key, comes from the entry itself. A key is never set per
launch.

Without `--backend`, the launcher resolves the `defaultBackend` pointer and
nothing else. It never falls back to the first entry, so a settings file with
entries but no default makes the launcher exit with exit code 8 until you run
`config set defaultBackend <name>`. A `--backend` name that matches no entry
also exits with exit code 8 and prints `unknown backend name '<name>'`, plus
a suggestion when a saved name is close.

### What `--persist config` actually does

Most harnesses read their settings from environment variables and leave no
trace on disk. `codex` is the exception. It reads its backend from
`config.toml`, so HarnessGateway writes a `model_providers.harnessgateway`
block there (`$CODEX_HOME/config.toml`, or `~/.codex/config.toml`) and passes
the key through the environment variable that the block names.

By default, the launcher copies your `config.toml` to a backup directory
before writing, then restores the original as soon as the `codex` process
exits, so your file comes back byte-identical. Pass `--persist config` to skip
the restore and keep the edited file. If the launcher dies before restoring,
the next launch replays the backup before doing anything else, so a crash
cannot leave a half-written config in place.

If `config.toml` already has a `model_providers.harnessgateway` block that you
wrote yourself, the launcher refuses and exits with exit code 10 rather than
adopting or overwriting it. Rename the block or point `CODEX_HOME` elsewhere.

`--persist model` saves the model the launch used as that entry's default.
Choosing `--backend` never promotes an entry. Only `config set defaultBackend`
does.

## Configuration

```bash
harnessgateway config show
harnessgateway config get <key>
harnessgateway config set <key> <value>
```

`config show` prints the default backend: its endpoint, protocol, and default
model, plus whether an API key is set, never the key itself. When no default
is set, the command says so and prints the command that fixes it.

### Typed keys

`config get` and `config set` work with a limited set of typed keys into the
settings file:

| Key | Access | Value |
|-----|--------|-------|
| `defaultBackend` | read/write | Name of the default backend entry. This is what `launch` resolves when no `--backend` is given |
| `schemaVersion` | read-only | Version of the settings-file schema, stamped by the loader |
| `backends` | read-only | Saved backend names, one per line. Never endpoints, protocols, or keys |
| `backends.<name>.endpoint` | read/write | Absolute URL of a saved entry |
| `backends.<name>.protocol` | read/write | `openai-chat`, `openai-responses`, or `anthropic` |
| `backends.<name>.defaultModel` | read/write | Per-entry default model |

A key outside that set exits with exit code 2: an unknown key, a key naming no
saved entry, or a write to `schemaVersion` or `backends`. Keys cannot create
or delete backends; the `backend add` and `backend remove` commands do that,
and no key can write secret material.

`config set defaultBackend <name>` is the only way to move the default
pointer. The command validates the name against the saved entries, prints
`Default backend set to '<name>'`, and succeeds without changing anything when
the pointer already names that entry. An unknown name exits with exit code 8,
as it does for `--backend`.

### Backends

The `config backend` subcommands create and manage the saved entries that
`launch` and `--backend` refer to:

```bash
harnessgateway config backend add <name> --endpoint URL --protocol PROTOCOL [--key SECRET]
harnessgateway config backend list
harnessgateway config backend remove <name>
harnessgateway config backend set-key <name> [--key SECRET]
harnessgateway config backend clear-key <name>
```

`backend add` creates an entry or replaces one of the same name. Both
`--endpoint` and `--protocol` are required, and a bad URL or unknown protocol
name exits with exit code 2. The command does not move the default pointer, so
run `config set defaultBackend <name>` after your first `add`; the success
message reminds you.

Without `--key`, `backend add` asks `Set an API key for '<name>'? [y/N]` on an
interactive terminal, or `Replace` when the entry already has a key, and hides
what you type. Answering no, pressing Enter at the hidden prompt, or piping
stdin leaves the entry keyless, which is valid. `--key` skips the prompt for
scripts but puts the secret in your shell history. A credential-store failure
exits with exit code 7 and writes nothing.

`backend list` marks the default entry with `*`. It prints a hint to add an
entry when the settings file is empty, or a hint to run `set defaultBackend`
when no default is set.

`backend remove` deletes the entry and its stored key. If you try to remove
the current default or the last remaining entry, the command refuses and exits
with exit code 2. Set a new default first.

`backend set-key` sets or replaces the saved API key for one backend. It
touches only the credential store, never the entry's endpoint, protocol, or
default model. It follows the same prompt rules as `backend add`, so `--key`
skips the hidden prompt. If you decline the prompt, `set-key` reports success
and writes nothing. A credential-store failure exits with exit code 7 and
writes nothing.

`backend clear-key` removes the saved key and leaves everything else about the
entry alone. Clearing a keyless backend succeeds and says there was nothing to
clear. A credential-store failure exits with exit code 7 and writes nothing.

If the name matches no saved entry, `remove`, `set-key`, and `clear-key` exit
with exit code 8.

## Where things are stored

| | |
|---|---|
| Windows | `%APPDATA%\HarnessGateway` |
| macOS | `~/Library/Application Support/HarnessGateway` |
| Linux | `$XDG_CONFIG_HOME/HarnessGateway`, else `~/.config/HarnessGateway` |

`HarnessGateway-launcher.json` holds the entry list, the default pointer, and
per-entry default models. An API key appears in the file only as a reference
like `hgkey:<name>`. The secret itself lives in DPAPI, the macOS keychain, or
the Linux secret service under that identifier.

When the loader opens a version-1 file, it migrates it automatically:
plaintext keys move into the credential store and are rewritten as references.
The migration is one-way, so an older build cannot read a migrated file
usefully. If a file's `schemaVersion` is missing, malformed, or newer than
this build, the loader refuses the file, exits with exit code 9, and leaves it
untouched.

## Exit codes

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded |
| 2 | UsageError | Invalid arguments, a launch with no backend configured, or a `remove` that would break the default pointer or empty the entry list |
| 3 | UnknownAgent | Unrecognized agent token, or a protocol the chosen harness doesn't speak. A near miss suggests the token you meant |
| 4 | HarnessUnavailable | The harness binary isn't on `PATH` |
| 5 | EndpointUnreachable | **Reserved.** Declared but never returned; nothing currently probes an endpoint's reachability |
| 6 | RequiredModelMissing | **Reserved.** Declared but never returned; the launcher warns about a harness that needs a model and has none, then launches it anyway |
| 7 | ConfigRestoreFailure | Credential-store failure, or an unhandled launch error. Messages from the unhandled path start with `Launch failed:` |
| 8 | UnknownBackendName | `--backend` named no entry, or the `defaultBackend` pointer dangles or is empty while entries exist |
| 9 | UnsupportedSettingsSchema | `schemaVersion` is missing, malformed, or newer than this build. Nothing is written |
| 10 | ConfigConflict | A user-authored `model_providers.harnessgateway` block already exists in the codex config |

Exit codes 5 and 6 are reserved and will not appear in this release. The enum
keeps them so a future build can claim those numbers without renumbering
anything documented here. New exit codes are appended only.

## License

[Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/).
