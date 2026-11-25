# Changelog

All notable changes to HarnessGateway are documented in this file.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and version headings follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html) in release-tag form (`0.1.0`). The heading matches the git tag the publish workflow asserts against the project's pinned version. Nothing enforces this format automatically. Release notes are written by hand when the maintainer creates the draft release, and the same curation writes the matching entry below, so the GitHub release page and this file cannot drift apart. Generated changelogs are not used.

## [0.1.2] - Unreleased

### Added

- One-shot non-interactive mode via `-p`/`--print` for harnesses that
  implement it (claude-code, opencode, kilo, copilot, codex, pi). A prompt comes
  from the flag value or stdin; GUI harnesses refuse.

### Fixed

- An interactive `codex` launch now stops codex's shared app-server daemon
  before the TUI starts. The TUI attaches to an already-running daemon, whose
  environment is fixed when it spawns, so the launch's API key never reached
  it: every request failed with `Missing environment variable:
  HARNESSGATEWAY_API_KEY` while the identical `-p`/`--print` launch worked.
  The TUI now starts a fresh daemon that inherits the launch's environment.
- Interactive TUI launches no longer paint into a hidden console on Windows:
  the child now shares the launcher's own console window, so Claude Code and
  other TUIs render where the user can see them instead of leaving a bare
  caret. Shell execution stays off, so the launch's environment variables
  (base URLs and keys) still reach the child.
- An interactive terminal launch now refuses with exit 13 when stdin or
  stdout is redirected or piped, instead of handing the child handles nobody
  can read. Pass `-p`/`--print "<prompt>"` for a one-shot non-interactive run.
- The `/responses` path segment is stripped only for harnesses that append it
  themselves, so an endpoint that already ends in `/responses` is not doubled
  for the OpenAI Chat Completions and Anthropic paths.

### Known Issues

- Windows only: any process the launcher starts - a harness, or the `where`
  / `which` availability probe every CLI harness runs before launching -
  can intermittently fail to spawn with `Win32Exception: Access is denied`.
  Where it lands decides what you see: an exit-7 `Launch failed:` error; an
  installed harness wrongly refused with the install hint (the probe
  swallows the fault); or, on an interactive `codex` launch, the same
  swallowed fault in the best-effort `codex app-server daemon stop` step,
  after which the TUI falls back to the stale daemon and reports
  `Missing environment variable: HARNESSGATEWAY_API_KEY` again. The cause
  is upstream in CliInvoke, which pins every child to CPUs 0-15 from its
  start hook; a child that exits first makes that call fail. Long-lived
  harnesses rarely hit it, but the test suite can flake on the Windows CI
  job until CliInvoke ships the fix and the dependency is bumped.

[0.1.2]: https://github.com/alastairlundy/HarnessGateway/compare/0.1.1...HEAD

## [0.1.1] - 2026-11-20

### Added

- The Windows MSI installer now adds its install directory to PATH for both
  scopes: the machine PATH on per-machine installs, the invoking user's PATH on
  per-user installs. The existing PATH is extended, never replaced, and
  uninstalling removes the entry. Already-open terminals need to be restarted
  to pick up the change.
- The wizard asks for the protocol before the endpoint URL when adding a fresh
  provider, so the endpoint prompt can match its hint to the protocol:
  OpenAI-style bases keep `/v1`, Anthropic-style bases omit it because Claude
  Code appends `/v1/messages` itself.
- A new "Endpoint conventions" section in the README documents the
  per-protocol `/v1` placement, the pass-through-unchanged policy, and the
  model-list request shape.

### Fixed

- The wizard no longer crashes with `Could not find color or style add` once
  any provider is configured: provider names read from settings and the
  literal `[add new provider]` choice are escaped before Spectre renders them
  as markup.
- Model-list requests keep the configured endpoint's base path and query. A
  base such as `https://host/my-gateway?x=1` now fetches
  `https://host/my-gateway/models?x=1` instead of discarding the prefix; a
  trailing `/models` segment is never doubled, a bare origin still fetches
  `/v1/models`, and a scheme-relative `//` path can no longer swap the host.
- The launch path's trailing-slash trim now touches only the URL's path
  portion, so a query ending in `/` survives intact.
- The wizard endpoint step and the saved-provider picker validate typed and
  stored base URLs as absolute http(s) URLs, refusing with a proper message
  instead of throwing `UriFormatException`.
- Release archives keep the documented `harnessgateway-<rid>.<ext>` names
  instead of carrying a `HarnessGateway.<version>.` segment.

[0.1.1]: https://github.com/alastairlundy/HarnessGateway/releases/tag/0.1.1

## [0.1.0] - 2026-11-20

### Added

- Initial public release of the `harnessgateway` launcher.

[0.1.0]: https://github.com/alastairlundy/HarnessGateway/releases/tag/0.1.0
