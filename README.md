# LmProxy

## Installation

### NuGet tool (cross-platform)

```bash
dotnet tool install --global lmlaunch
```

### Per-OS binaries

Download the archive for your platform from [GitHub Releases](https://github.com/alast/LmProxy/releases):

| Platform | Archive |
|----------|---------|
| Linux x64 | `lmlaunch-linux-x64.tar.gz` |
| Windows x64 | `lmlaunch-win-x64.zip` |
| macOS x64 | `lmlaunch-osx-x64.tar.gz` |
| macOS ARM64 | `lmlaunch-osx-arm64.tar.gz` |

Extract the archive and add the directory to your `PATH`.

## Usage

```bash
lmlaunch launch <agent> [--endpoint URL --protocol PROTOCOL] [--model MODEL] [--persist config|model]
```

## Exit codes

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded |
| 2 | UsageError | Invalid arguments or missing required options |
| 3 | UnknownAgent | Requested agent harness is not recognised |
| 4 | HarnessUnavailable | Agent harness binary not found on PATH |
| 5 | EndpointUnreachable | Backend endpoint could not be reached |
| 6 | RequiredModelMissing | No model was specified and no default is configured |
| 7 | ConfigRestoreFailure | Failed to restore configuration after a no-persist launch |
