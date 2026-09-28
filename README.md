# HarnessGateway

## Installation

### NuGet tool (cross-platform)

```bash
dotnet tool install --global harnessgateway
```

### Per-OS binaries

Download the archive for your platform from [GitHub Releases](https://github.com/alast/HarnessGateway/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `harnessgateway-linux-x64.tar.gz` |
| Windows x64 | `harnessgateway-win-x64.zip` |
| macOS x64 | `harnessgateway-osx-x64.tar.gz` |
| macOS ARM64 | `harnessgateway-osx-arm64.tar.gz` |

Extract the archive and add the directory to your `PATH`.

## Usage

```bash
harnessgateway launch <agent> [--backend NAME] [--endpoint URL --protocol PROTOCOL] [--model MODEL | -m MODEL] [--persist config|model]
```

`-m` is the short alias for `--model`; no other short flags are defined.

`--backend <name>` selects a saved backend entry to launch against. `--endpoint`, `--protocol`, and `--model` then layer on top of the resolved entry field by field: each flag replaces only its own field, and every other field - including the entry's stored API key - comes from the saved entry. Keys are not overridable per launch; the resolved entry's stored key is the only credential source.

Without `--backend`, launch resolves the entry named by the `defaultBackend` pointer and nothing else: no first-entry fallback and no auto-promotion. A `--backend` name or default pointer that resolves no saved entry fails with exit code 8 and the message `unknown backend name '<name>'`; an unresolvable default pointer directs you to `harnessgateway config backend switch`. Best-effort "did you mean" hints may accompany the message but are non-contractual.

`--persist` accepts exactly `config` and `model`. A `--backend` launch persists nothing about its selection - changing the default backend remains `config backend switch`'s job.

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
| 10 | ConfigConflict | Launch was refused because existing configuration conflicts with the launcher's writes (for example a pre-existing provider block). Resolve the conflict and launch again |
