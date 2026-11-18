# Privacy policy

**Version 1. Effective 18 November 2026.** The law of England and Wales applies
to this policy. The maintainers are based in the United Kingdom, and UK GDPR
and the Data Protection Act 2018 apply to us.

This policy covers the `harnessgateway` program — the launcher distributed from
this repository's releases, the MSI installers, and WinGet — and the contact
channels listed in [Contact](#contact). It does not cover the harnesses we
support (which may have their own privacy policies), the operator of any
endpoint you configure, or the GitHub Releases and WinGet distribution
services themselves. Your use of GitHub Releases and WinGet is subject to
their respective privacy policies.

In this policy, "we", "us" and "our" mean the maintainers of HarnessGateway
(Alastair Lundy).

## Data the program stores and sends

HarnessGateway has no telemetry, no analytics, no crash reporting, and no
usage statistics. No request from it goes to a service the maintainers
operate, and the maintainers receive no data from it. The program stores
information locally on your device; the maintainers never receive that
information and never store it. We hold no data about you from the program
itself; we do hold messages you send through the contact channels described
under [Contact data we hold](#contact-data-we-hold).

The table below lists everything the program stores or sends.

| What | Where the information is stored/sent | Nature of the information | Retention | Notes |
|---|---|---|---|---|
| Settings file contents | `HarnessGateway-launcher.json` in the per-OS config dir: `%APPDATA%\HarnessGateway` on Windows, `~/Library/Application Support/HarnessGateway` on macOS, `$XDG_CONFIG_HOME/HarnessGateway` else `~/.config/HarnessGateway` on Linux. It holds provider names, endpoints, protocols, per-entry default models, each provider's model catalog (model names and optional per-model protocol overrides), `schemaVersion`, the `defaultProvider` pointer, and each saved API key as an opaque reference such as `hgkey:<name>` | Not personally identifiable on its own; it can become identifiable if combined with an identifier | Until you delete it | |
| Saved API keys | Your OS credential store: on macOS, generic-password items in the login keychain under the service `HarnessGateway`; on Linux, the Secret Service keyring via `secret-tool`; on Windows, `HarnessGateway-keys.json` in the config directory, where keys are stored as DPAPI-encrypted ciphertext bound to your Windows user account. Never in the settings file as plaintext | Personally identifiable | Until you delete it with `provider remove`, `provider clear-key`, or manually (see [How long we retain it](#how-long-we-retain-it)) | The Windows file is named so you can find and delete it |
| Wizard session key | Held in memory for one interactive wizard invocation and passed to the harness process as an environment variable on the child process. Never written to the settings file or the credential store | A credential you supplied; not retained by the program | Discarded when that invocation ends | Only applies if you type a key into the interactive wizard. An API key is never persisted; `--persist config` keeps harness configuration only, never a key |
| Update check timestamp | `update-check.timestamp`, next to the settings file, used for the 10-minute throttle | Not personally identifiable on its own | Until the next check that reaches the network overwrites it | |
| Codex config edit and its backup | A `model_providers.harnessgateway` block in your `config.toml` (`$CODEX_HOME/config.toml`, else `~/.codex/config.toml`), plus a backup marker in the `backups/` subdirectory of the config directory holding the original file's bytes | May contain personally identifiable information. The backup is a byte-for-byte copy of your original `config.toml`, which can contain API keys and other provider configuration you wrote yourself | The backup is deleted when the launch ends, either restored or discarded under `--persist config`. A leftover from a crashed launch is replayed and deleted on the next launch | Only applies if you use the Codex harness (OpenAI's Codex CLI). The block itself contains the endpoint and model, not the key |
| Migration temp file | `HarnessGateway-launcher.json.migrating`, next to the settings file, created while a legacy settings file is rewritten | Holds the migrated settings shape; never a plaintext key | Deleted when the rewrite completes; a failed write leaves the old file intact | Only applies to a settings file from an older build |
| Model list request | One `GET /v1/models` request sent to the endpoint you configured by the interactive wizard's model step | Your IP address is personally identifiable. When a key is available, the request also carries it as a credential | Held by the operator of that endpoint, not by the program or the maintainers. That operator's privacy policy governs it | The request is shaped by the protocol you configured: `Authorization: Bearer <key>` for OpenAI-protocol endpoints and `x-api-key: <key>` for Anthropic-protocol ones. The request happens whether or not a key is saved; with no key, the authentication header is omitted but your IP address still reaches the endpoint. No `User-Agent` header is sent on this request |
| Update check request | One HTTPS GET to `https://api.github.com/repos/alastairlundy/HarnessGateway/releases/latest` | Your IP address is personally identifiable | Held by GitHub, not by the program or the maintainers. [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement) governs it; we don't control how long GitHub keeps its logs | GitHub sees your IP address, a `User-Agent` header reading `HarnessGateway/<version>`, and an `Accept` header reading `application/vnd.github+json` |

A settings file from an older build that held API keys in plaintext is migrated
the first time settings are loaded (not only at launch): the API key moves into
the credential store and the file is rewritten with the reference. The backup
taken during that migration holds the swept shape, never the plaintext key.

### Contact data we hold

The maintainers do not receive or store data from the program. We do hold
messages you send us through the contact channels in [Contact](#contact): your
email address or GitHub account, the message content, and anything else you
choose to include. The controller for this data is the maintainers of
HarnessGateway (Alastair Lundy); our contact details are in
[Contact](#contact). This is the only information we hold about you, and you
can exercise your rights over it (see [Your rights](#your-rights)).

| What | Where it is held | Retention |
|---|---|---|
| GitHub Discussions you open | By GitHub, in the repository's Discussions. Discussions are public | Indefinite. We don't delete them |
| Emails you send to `harnessgateway@alastair.mozmail.com` | The project's email inbox | As long as is reasonably necessary for record keeping and spam prevention |

GitHub is the controller for a Discussion and its privacy policy governs it.
Please don't post personal information you wouldn't want to be public.

## Where data goes

The program makes two kinds of outbound request: one update check, and one
model-list fetch in the wizard's model step. Everything else it does is
local. The harnesses we support make their own requests once the program
starts them; their handling of your data is outside this policy.

### The update check

`harnessgateway update check` sends one HTTPS GET to
`https://api.github.com/repos/alastairlundy/HarnessGateway/releases/latest`.
GitHub sees your IP address, the `User-Agent` header, which reads
`HarnessGateway/<version>`, and the `Accept` header, which reads
`application/vnd.github+json`. Checks are throttled to one network call per 10
minutes; `--force` skips the wait. The command compares versions and prints
the result. It downloads, installs, and applies nothing.

The check runs only when you run the command. The program never checks
automatically. If you don't want this request to happen, don't run
`harnessgateway update check`. We receive nothing from it.

### The model list fetch

The interactive wizard's model step makes one `GET /v1/models` request to the
endpoint you configured so it can offer you model choices. The request is
shaped by the protocol you configured: OpenAI-protocol endpoints use
`Authorization: Bearer <key>`, and Anthropic-protocol endpoints use
`x-api-key: <key>`. Only one request is made, with the header style matching
that protocol, so a key is never sent under a header style the endpoint does
not expect. The request is made whether or not a key is available: when none is
saved, the authentication header is omitted, but your IP address still reaches
the endpoint. It carries no `User-Agent` header and goes only to the endpoint
you configured. If it fails, the wizard reports the failure and falls back to
free-text model entry. The operator of that endpoint is the controller for
what it receives; their privacy policy governs it.

### Handoff to the harness

The program never talks to a model endpoint during a launch. It starts the
harness you named. The harness then sends its own requests to the endpoint you
configured, carrying the API key — either the key you saved on a provider
entry or a key you typed into the wizard for that session. The program passes
that key to the harness process as an environment variable on the child
process. For the `codex` harness (OpenAI's Codex CLI) it also writes a
`model_providers.harnessgateway` block to your `config.toml` that points at
that environment variable.

### International transfers

Two kinds of transfer can leave the UK. The update check sends your IP address
to GitHub in the United States. The model-list fetch and your harness's
requests go to the operator of the endpoint you configured, which may be in
any country. Each recipient is the controller for what it receives, and its own
safeguards and privacy policy govern the transfer and its retention period —
for example [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).
We don't control those recipients' retention periods, and we receive no data
from either transfer. Check your endpoint operator's policy before configuring
it.

## How long we retain it

We retain no information about you from the program itself. Data the program
stores locally on your device stays there until you delete it. An API key is
never persisted: `--persist config` keeps only harness configuration (such as
the Codex `config.toml` edit), never a key. The only information we hold is
the contact-channel data described above, with the retention periods in that
table.

To remove provider information from the program, clear keys first, then remove
entries, then delete the files:

- `provider clear-key <name>` deletes just the API key. It needs a matching
  saved entry, so run it before you remove the entry or delete the settings
  file.
- `provider remove <name>` deletes an entry and its stored API key. It refuses
  to remove the current default or the last remaining entry; set a new default
  first.
- Deleting `HarnessGateway-launcher.json` clears the entries and the default
  pointer, but it does not delete credential-store entries. Those become
  orphaned, with no command left to remove them, unless you cleared the keys
  first.

To purge credential-store entries manually:

- **Windows:** delete `HarnessGateway-keys.json` from `%APPDATA%\HarnessGateway`.
  It holds every saved key as DPAPI-encrypted ciphertext.
- **macOS:** remove the `HarnessGateway` generic-password items in Keychain
  Access.
- **Linux:** remove the Secret Service items with `secret-tool` (for example
  `secret-tool clear key <provider-name>`) or a keyring manager such as
  Seahorse.

Uninstalling the program removes the program files only. The settings file,
`HarnessGateway-keys.json` on Windows (or the keyring entries on macOS and
Linux), `update-check.timestamp`, the `backups/` directory, and any
credential-store entries remain. Delete them manually if you want them gone.

## Our legal basis

UK GDPR and the Data Protection Act 2018 apply to us. We don't rely on consent
for the program, and we don't ask you to consent to this policy. The program
runs locally on your device, and because we neither receive nor hold the data
it stores there, we are not a controller for that local data. You can stop
using the program at any time; you don't need to uninstall it.

The processing described in this policy falls into these categories:

| Activity | Who is the controller | Legal basis |
|---|---|---|
| Storing settings and keys locally on your device | The maintainers are not a controller; the data stays on your device | No basis is needed from us — we don't process it |
| `harnessgateway update check` sending your IP address and headers to GitHub | GitHub | The check runs only when you run the command. We neither receive nor retain anything from it. GitHub's own basis and privacy policy govern what it does |
| The wizard's model-list fetch and the harness's requests to your endpoint | The operator of the endpoint you configured | Your agreement with that operator, and their privacy policy. We receive nothing from it |
| Messages you send us by email or through GitHub Discussions | The maintainers | **Legitimate interests** — responding to your enquiry, record keeping, and spam prevention |

If a future release adds an automatic update check, we will state the legal
basis and your right to object before it ships.

## Your rights

Because we hold no information about you from the program itself, there is
nothing for us to access, correct, delete, or export in relation to your use
of it. Data the program stored on your device is yours to delete (see
[How long we retain it](#how-long-we-retain-it)). You can stop using the
program at any time.

For the messages you send us through the contact channels, we are the
controller and you can exercise these rights:

- Your right of access — you have the right to a copy of the data we hold
  about you
- Your right to rectification — you have the right to ask us to correct
  inaccurate data
- Your right to erasure — you have the right to ask us to delete the data we
  hold on you
- Your right to restriction of processing — you have the right to ask us to
  limit how we use your personal information
- Your right to data portability — you have the right to receive the data you
  gave us in a portable format
- Your right to object to processing — you have the right to object to our
  processing based on legitimate interests
- Your right to withdraw consent — where we rely on consent, you can withdraw
  it at any time
- Your right to lodge a complaint — you have the right to complain to the
  Information Commissioner's Office (ICO), the UK supervisory authority, at
  https://ico.org.uk/make-a-complaint/

To exercise any of these rights, email us at
harnessgateway@alastair.mozmail.com.

For the data an AI inference service holds about you, contact the operator of
the endpoint you configured to ask about exercising your rights under data
protection law or their privacy policy. Examples of such rights may include:

- Your right of access — you have the right to a copy of the data it stores
  about you
- Your right to erasure — you have the right to ask the AI inference provider
  to delete the data it holds on you
- Your right to object to processing — you have the right to object to the
  processing of your data
- Your right to restriction of processing — you have the right to ask the
  provider to limit how they can use your personal information
- Your right to withdraw consent — you have the right to withdraw your
  consent to the use of the AI inference service at any time

Which laws apply to a provider depends on where that provider is established
and whether it offers services to, or monitors, people in the UK or EU — not
solely on where you live. For details on how to exercise your rights with your
AI provider, view that provider's privacy policy.

To the extent that a harness has a Privacy Policy or is governed by one, you
can ask the relevant controller about exercising your rights.

## Children

The program does not target or aim to attract individuals under the age of 18.
We don't knowingly collect personal data from children through the contact
channels. Many AI inference services are not available to people under 16;
check the terms of any service you configure.

## Changes to this policy

Changes land in this repository and ship with a release. This is Version 1,
effective 18 November 2026; the version and effective date above change when
the policy does.

## Contact

This policy also covers the contact channels below. If you have questions
about our privacy policy, please open a discussion at
https://github.com/alastairlundy/HarnessGateway/discussions. GitHub is the
controller for a Discussion and its privacy policy governs it. Discussions are
public and retained indefinitely, so please don't post personal information
you wouldn't want to be public.

For privacy enquiries, to exercise your rights over the data we hold, or for
any other issues you would like to contact the maintainers about, please
email: harnessgateway@alastair.mozmail.com. These are our contact details for
the purposes of UK GDPR. We keep email for as long as is reasonably necessary
for record keeping and spam prevention. Reports of Code of Conduct violations
are handled through a separate channel; see CODE_OF_CONDUCT.md.
