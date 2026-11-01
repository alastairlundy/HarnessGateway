# HarnessGateway

<div align="center">

[![License](https://img.shields.io/badge/license-MPL--2.0-blue.svg)](https://www.mozilla.org/en-US/MPL/2.0/)
[![Latest release](https://img.shields.io/github/v/release/alastairlundy/HarnessGateway)](https://github.com/alastairlundy/HarnessGateway/releases)
[![Downloads (latest release)](https://img.shields.io/github/downloads/alastairlundy/HarnessGateway/total)](https://github.com/alastairlundy/HarnessGateway/releases)

</div>

HarnessGateway is a command-line launcher for agent harnesses. Point it at an
OpenAI- or Anthropic-compatible endpoint, name the harness to run, and it
starts that harness against your endpoint. Credentials go in your OS keychain,
never in the settings file.

## Install

Download the archive for your platform from
[GitHub Releases](https://github.com/alastairlundy/HarnessGateway/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `harnessgateway-linux-x64.tar.gz` |
| Linux ARM64 | `harnessgateway-linux-arm64.tar.gz` |
| Windows x64 | `harnessgateway-win-x64.zip` |
| Windows ARM64 | `harnessgateway-win-arm64.zip` |
| macOS x64 | `harnessgateway-osx-x64.tar.gz` |
| macOS ARM64 | `harnessgateway-osx-arm64.tar.gz` |

Unpack it wherever you like and put the directory on your `PATH`. The binaries
are self-contained, so you don't need to install .NET.

Linux also has native packages for both architectures:

| Distro | Package |
|--------|---------|
| Debian / Ubuntu | `harnessgateway-linux-x64.deb`, `harnessgateway-linux-arm64.deb` |
| Fedora / RHEL | `harnessgateway-linux-x64.rpm`, `harnessgateway-linux-arm64.rpm` |

These `.deb` and `.rpm` packages are unsigned. The build pipeline doesn't
carry a GPG key, so there's nothing to import and no repository to add.
Install the file you downloaded directly, with
`dpkg -i harnessgateway-linux-x64.deb` or
`rpm -i harnessgateway-linux-x64.rpm` (`dnf install ./<package>` also works).
Your package manager will warn about the missing signature; that's expected,
and it's why you should only download from the GitHub Releases page above.
The plain archives install the same result on every platform and skip that
warning entirely.

macOS and Windows have their own checks, though. The build pipeline carries
no Apple Developer ID and no Authenticode certificate, so nothing is signed
or notarized.

On macOS, Gatekeeper blocks the first launch of a binary that a browser
downloaded and shows a "cannot verify" dialog instead. Open
System Settings > Privacy & Security, find the blocked launch of
`harnessgateway`, and click Open Anyway. After that, the binary launches
normally. Downloading with `curl`, or unpacking in a terminal, skips the
check, since only browsers mark files for quarantine.

On Windows, SmartScreen shows "Windows protected your PC" on first launch.
Click More info, then Run anyway.

Neither prompt means anything is wrong with the binary. It is the same
missing signature as above, and both systems ask again for each new release,
since every build produces a new binary.

## Harnesses

HarnessGateway launches seven agent harnesses, none of which come bundled
with it. Install whichever ones you want from the table below and the
launcher will find them: CLI harnesses through a `PATH` lookup, GUI harnesses
through that lookup plus your OS's conventional install locations.

| Harness Identifier | Harness Name | Binary | Protocols | Model | Install |
|-------|------|--------|-----------|-------|---------|
| `opencode` | OpenCode CLI | `opencode` | chat, responses, anthropic | required | `npm install -g @opencode/cli` |
| `kilo` | Kilo CLI | `kilo` | chat, responses, anthropic | required | `npm install -g @kilocode/cli` |
| `copilot` | GitHub Copilot CLI | `copilot` | chat, responses, anthropic | required | `npm install -g @github/copilot` |
| `pi` | Pi | `pi` | chat, responses, anthropic | optional | `npm install -g @earendil-works/pi-coding-agent` |
| `codex` | Codex CLI | `codex` | responses | required | `npm install -g @openai/codex` |
| `claude-code` | Claude Code (CLI) | `claude` | anthropic | optional | `npm install -g @anthropic-ai/claude-code` |
| `opencode-desktop` | OpenCode Desktop (GUI app) | `OpenCode.exe` (Windows), `OpenCode.app` (macOS), `ai.opencode.desktop` (Linux) | chat, responses, anthropic | required | `brew install --cask opencode-desktop`, or download from https://opencode.ai/download |

"chat" is shorthand for `openai-chat` and "responses" for `openai-responses`.
If you request a protocol that the harness doesn't support, the launcher
exits with exit code 3 and prints the protocols that harness supports.

The **Harness Identifier** column holds what you actually type:
`harnessgateway launch copilot`. The **Harness Name** column is only a display
label, used by the wizard and quoted in error messages; the launcher does not
accept it as input. This distinction is easy to miss because the identifier
and the name match for most harnesses. It bites with Copilot, whose name is
"GitHub Copilot CLI" but whose identifier is `copilot`, and with OpenCode
Desktop, whose identifier is `opencode-desktop`. Matching is
case-insensitive, so `launch COPILOT` works too. Aliases are also
accepted: `claude` for `claude-code` and `github-copilot-cli` for `copilot`.
The `--help` output lists harness identifiers only.

Detection for CLI harnesses is a simple `PATH` lookup, which means a
harness that's installed but not on your `PATH` looks missing. Check with
the binary's own name:

```bash
opencode --version
copilot --version
```

GUI harnesses are located differently: the launcher asks your operating
system where the app is, checking your `PATH` first and then the
conventional install locations for your platform (`Program Files` on
Windows, `/Applications` on macOS, `/usr/bin` on Linux). A GUI app
installed anywhere else - a portable install, say - looks missing.

If you name a harness that isn't found, `launch` exits with exit code 4 and
prints the install command for that harness. In the wizard, undetectable
harnesses are labeled `not installed`, and the same hint is printed if you
pick one anyway.

HarnessGateway never edits your shell profile or its own environment. Every
setting in the table below is passed only to the process it starts:

| Harness Identifier | How it's configured |
|-------|---------------------|
| `opencode`, `kilo`, `pi` | `OPENAI_*` or `ANTHROPIC_*` env vars |
| `copilot` | `COPILOT_PROVIDER_BASE_URL`, `COPILOT_PROVIDER_API_KEY`, `COPILOT_PROVIDER_TYPE` |
| `claude-code` | `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_MODEL` |
| `codex` | a `model_providers.harnessgateway` block written to `config.toml` |
| `opencode-desktop` | `OPENAI_*` or `ANTHROPIC_*` env vars plus `OPENCODE_CONFIG_CONTENT`, an inline session config the app's sidecar server reads. The launcher never writes the app's own config file |

## Usage

```bash
harnessgateway launch [HARNESS] [options]
```

| Option | Effect |
|--------|--------|
| `--backend NAME` | Launch against a saved backend entry instead of the default one |
| `--endpoint URL --protocol PROTOCOL` | Override the endpoint and protocol of the selected entry |
| `--model MODEL`, `-m MODEL` | Override the model for this launch only |
| `--persist config` | Keep the codex `config.toml` edit instead of restoring the original on exit; for a GUI harness, also switches the launch to fire-and-forget mode |
| `--persist model` | Save this launch's model as the entry's default for later launches |
| `--confirm-running` | Assert that launching while the harness's app is already running was confirmed, skipping the prompt (GUI harnesses; see [Launching a GUI harness](#launching-a-gui-harness)) |

`--endpoint` and `--protocol` only make sense together; either one alone is a
parse error. `--persist` takes both values at once (`--persist config model`)
or comma-separated (`--persist config,model`).

```bash
harnessgateway --version
```

`harnessgateway --version` prints your build's version number, for example
`0.1.0`, and exits with exit code 0. Quote it when you report a bug. The
version never appears in the archive file names, and it has no connection to
the settings `schemaVersion` that `config get schemaVersion` reports.

Run `harnessgateway` with no arguments, or `harnessgateway launch` with no
harness identifier and no options, and you get the interactive wizard
instead. The wizard
prompts for the harness, endpoint, key, and model, in that order. For the model step it
fetches the endpoint's model list to fill in your choices, and falls back to
free text if that request fails. Nothing it collects gets saved. Options
without a harness are a parse error, not a silently ignored flag. An aborted
wizard run exits with the same code the direct `launch` path returns for the
equivalent condition: 0 when you cancel, 3 when no installed harness speaks
the chosen protocol, 4 when nothing is installed, 8 for a default-backend
pointer problem, 7 for a credential-store failure. Exit status alone cannot
tell you which entry point you ran.

### Choosing a backend

`--backend NAME` picks a saved entry, and `--endpoint`, `--protocol`, and
`--model` each override one field of that entry. Everything else, including
the entry's saved key, comes from the entry itself. There is no per-launch
key.

Without `--backend`, the launcher uses only the `defaultBackend` pointer. It
never falls back to the first entry, and the two pointer failures are
distinct refusals — both exit with exit code 8:

- **DanglingDefaultBackend** — the default-backend pointer is set but names no
  saved entry.
- **AbsentDefaultBackend** — the pointer is empty while saved entries exist —
  no first-entry fallback.

Run `config set defaultBackend <name>` to fix either one. A `--backend` name
that matches no entry is the third exit-code-8 refusal; it prints
`unknown backend name '<name>'` plus a similar-name suggestion when a saved
name is close.

### What `--persist config` actually does

Most harnesses read their settings from environment variables and leave no
trace on disk. `codex` is the exception. It reads its backend from
`config.toml`, so HarnessGateway writes a `model_providers.harnessgateway`
block there (`$CODEX_HOME/config.toml`, or `~/.codex/config.toml` by default)
and passes the key through the environment variable that block names.

By default, the launcher backs up your `config.toml` before writing and
restores the original as soon as the `codex` process exits, so your file
comes back byte-identical. Pass `--persist config` to skip the restore and
keep the edited file. If the launcher dies before it can restore, the next
launch replays the backup before doing anything else, so a crash can't leave
a half-written config behind.

If your `config.toml` already contains a `model_providers.harnessgateway`
block that you wrote yourself, the launcher makes no changes to it and exits
with exit code 10 rather than adopt or overwrite it. Rename the block or
point `CODEX_HOME` elsewhere.

`--persist model` saves the model this launch used as the entry's default.
Note that choosing `--backend` never promotes an entry to default. Only
`config set defaultBackend` does that.

### Launching a GUI harness

`opencode-desktop` is the first GUI harness. It launches like any other
(`harnessgateway launch opencode-desktop`), with three differences.

**`--persist config` also selects the launch mode.** With it, the launcher
applies the configuration, starts the app, and returns immediately with
exit code 0 while the app keeps running. Without it, the launcher blocks
until you quit the app, and everything it wrote is restored on exit -
exactly as a CLI launch restores the codex edit. The restore guarantee is
the same in both modes; only the timing differs.

**A running instance asks for confirmation.** If the app is already open,
`launch` prints a `Proceed anyway? [y/N]` question, because configuration
applied to an open application may be overwritten or ignored by the
instance that is running. Declining - and a non-interactive launch, where
stdin is piped and nothing can answer the question - stops the launch with
exit code 4 and writes nothing. Pass `--confirm-running` to assert the
answer yourself and skip the prompt in every context, interactive or not:

```bash
harnessgateway launch opencode-desktop --persist config --confirm-running
```

Using `--confirm-running` with a CLI harness is a usage error (exit code 2):
CLI harnesses have no running instance to confirm.

**Keys never touch disk.** A saved API key is handed to the app as an
environment variable on the child process - only when the harness declares
that surface - and the launcher never writes key material to a
configuration file. A key-carrying backend pointed at a GUI harness that
declares no environment surface refuses with exit code 4, as does a
backend or model override against a harness that declares no configuration
surface that could carry it. Keys still come only from saved backend
entries; there is no per-launch key for GUI harnesses either.

## Configuration

```bash
harnessgateway config show
harnessgateway config get <key>
harnessgateway config set <key> <value>
```

`config show` prints the default backend: its endpoint, protocol, and default
model, plus whether an API key is set. It never prints the key itself. When
no default is set, the command reports that and prints the command that
fixes it.

### Typed keys

`config get` and `config set` work with a limited set of typed keys:

| Key | Access | Value |
|-----|--------|-------|
| `defaultBackend` | read/write | Name of the default backend entry. This is what `launch` resolves when no `--backend` is given |
| `schemaVersion` | read-only | Version of the settings-file schema, stamped by the loader |
| `backends` | read-only | Saved backend names, one per line. Never endpoints, protocols, or keys |
| `backends.<name>.endpoint` | read/write | Absolute URL of a saved entry |
| `backends.<name>.protocol` | read/write | `openai-chat`, `openai-responses`, or `anthropic` |
| `backends.<name>.defaultModel` | read/write | Per-entry default model |

Using any other key exits with code 2. That includes an unknown key, a key
that matches no saved entry, and a write to a read-only key (`schemaVersion`
or `backends`). Keys can't create or delete backends; the `backend add` and
`backend remove` commands do that. No key can write secret material.

`config set defaultBackend <name>` is the only way to move the default
pointer. The command checks the name against your saved entries, prints
`Default backend set to '<name>'`, and succeeds without changing anything if
the pointer already names that entry. An unknown name exits with code 8, as
do both default-pointer refusals the command exists to fix:
DanglingDefaultBackend (the default-backend pointer is set but names no saved
entry) and AbsentDefaultBackend (the pointer is empty while saved entries
exist — no first-entry fallback).

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

`backend add` creates an entry, or replaces an existing one of the same name.
Both `--endpoint` and `--protocol` are required. A malformed URL or an
unknown protocol name exits with code 2. The command doesn't move the
default pointer, so run `config set defaultBackend <name>` after your first
`add`; the success message says the same thing.

Without `--key`, `backend add` shows a `Set an API key for '<name>'? [y/N]`
prompt on an interactive terminal, or a `Replace` prompt when the entry
already has a key, and hides what you type. Answering no, pressing Enter at
the hidden prompt, or piping input through stdin all leave the entry keyless,
which is perfectly valid. `--key` skips the prompt, which is convenient for
scripts but puts the secret in your shell history. If talking to the
credential store fails, the command exits with code 7 and writes nothing.

`backend list` marks the default entry with `*`. It prints a hint to add an
entry when the settings file is empty, or a hint to run `set defaultBackend`
when there are entries but no default.

`backend remove` deletes the entry and its stored key. The command exits with
code 2 rather than remove the current default or the last remaining entry.
Set a new default first.

`backend set-key` sets or replaces the saved API key for one backend. It
touches only the credential store, never the entry's endpoint, protocol, or
default model. The prompt rules are the same as for `backend add`, so
`--key` skips the hidden prompt. If you decline the prompt, `set-key`
reports success and writes nothing. A credential-store failure exits with
code 7 and writes nothing.

`backend clear-key` removes the saved key and leaves everything else about
the entry alone. Clearing a keyless backend succeeds and reports that there
was nothing to clear. A credential-store failure exits with code 7 and writes
nothing.

For `remove`, `set-key`, and `clear-key`, a name that matches no saved entry
exits with code 8.

## Where things are stored

| | |
|---|---|
| Windows | `%APPDATA%\HarnessGateway` |
| macOS | `~/Library/Application Support/HarnessGateway` |
| Linux | `$XDG_CONFIG_HOME/HarnessGateway`, else `~/.config/HarnessGateway` |

`HarnessGateway-launcher.json` holds the entry list, the default pointer, and
per-entry default models. An API key shows up in the file only as an opaque
reference like `hgkey:<name>`. The secret itself lives in DPAPI, the macOS
keychain, or the Linux secret service under that identifier.

When the loader opens a version-1 file, it migrates it automatically:
plaintext keys move into the credential store and get rewritten as
references. The migration is one-way, so older builds can't read a migrated
file usefully. If a file's `schemaVersion` is missing, malformed, or newer
than this build, the loader rejects the file, exits with exit code 9, and
leaves it untouched.

## Exit codes

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded; for `launch`, the harness process itself exited 0 — or, for a detached GUI launch (`--persist config`), the app was handed off successfully and there is no exit status to wait for |
| 2 | UsageError | Invalid arguments, an incomplete `--endpoint`/`--protocol` pair or invalid override values, a launch with nothing configured (empty settings and no complete override pair), a `remove` that would break the default pointer or empty the entry list, or `--confirm-running` against a CLI harness, which has no running instance to confirm (ConfirmUnsupportedForHarness) |
| 3 | UnknownAgent | Unrecognized harness identifier, or a protocol the chosen harness doesn't support. A near miss prompts a suggestion of the correct identifier |
| 4 | HarnessUnavailable | The harness is known but not installed or not detectable on `PATH` (for a GUI harness: not found on `PATH` or in the OS's conventional install locations), or a GUI launch stopped because an already-running instance was not confirmed (RunningInstanceUnconfirmed), or because the harness's declared configuration surfaces cannot carry the launch's key or overrides (GuiBackendUnsupported) |
| 5 | EndpointUnreachable | **Reserved.** Declared but never returned; nothing currently probes an endpoint's reachability |
| 6 | RequiredModelMissing | **Reserved.** Declared but never returned; the launcher prints a warning when a harness requires a model but has none, then launches it anyway |
| 7 | ConfigRestoreFailure | Credential-store failure (absent key or store error), or an unexpected fault inside the launch flow — including a config restore that fails. Messages from the unexpected path start with `Launch failed:` |
| 8 | UnknownBackendName | `--backend` matched no entry (unknown backend name), the default-backend pointer is set but names no saved entry (DanglingDefaultBackend), or the pointer is empty while saved entries exist (AbsentDefaultBackend — no first-entry fallback) |
| 9 | UnsupportedSettingsSchema | `schemaVersion` is missing, malformed, or newer than this build. Nothing is written |
| 10 | ConfigConflict | A user-authored `model_providers.harnessgateway` block already exists in the codex config |

Exit codes 5 and 6 are reserved and won't appear in this release. The enum
keeps them so that a future build can claim those numbers without
renumbering anything documented here. New exit codes are appended only. On a
launch, the harness process's own exit status is passed through unchanged,
so a number the harness itself returns can coincide with any code above.

The three GUI refusals are producers of the existing numbers above, not new
codes: RunningInstanceUnconfirmed and GuiBackendUnsupported exit 4 like
every other unavailable-harness stop, and ConfirmUnsupportedForHarness
exits 2 like every other usage error. Scripts that need to tell those
stops apart should read the stderr message, which names the reason - the
exit code alone cannot.

## License

[Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/).
