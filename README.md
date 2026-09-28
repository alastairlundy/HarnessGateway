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
harnessgateway launch <agent> [--endpoint URL --protocol PROTOCOL] [--model MODEL | -m MODEL] [--persist config|model]
```

`-m` is the short alias for `--model`; no other short flags are defined.

## Exit codes

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded |
| 2 | UsageError | Invalid arguments or missing required options |
| 3 | UnknownAgent | Requested agent harness is not recognised, or the selected backend's protocol is not supported by that harness |
| 4 | HarnessUnavailable | Agent harness binary not found on PATH |
| 5 | EndpointUnreachable | Backend endpoint could not be reached |
| 6 | RequiredModelMissing | No model was specified and no default is configured |
| 7 | ConfigRestoreFailure | Failed to restore configuration after a no-persist launch |
| 10 | ConfigConflict | Launch was refused because existing configuration conflicts with the launcher's writes (for example a pre-existing provider block). Resolve the conflict and launch again |
