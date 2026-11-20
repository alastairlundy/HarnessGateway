# Maintainer WinGet manifest runbook

Every HarnessGateway release gets a WinGet manifest PR to the official community source, `microsoft/winget-pkgs`. This runbook is the per-release procedure. Generate the manifest files with Komac from the published MSI (Microsoft Windows Installer) URLs and their SHA-256 hashes, validate them locally with `winget validate`, then open a maintainer-owned pull request (PR) for that version. Open one PR per release, right after the dispatch workflow publishes. Everything runs on the maintainer's machine, and continuous integration (CI) stays write-free. No CI credential ever writes to
`microsoft/winget-pkgs`.

## Package identity

The canonical package identity, reused verbatim by every step below:

- **PackageIdentifier**: `AlastairLundy.HarnessGateway`. Keep this casing
  unchanged. It is a deliberate pick, not the lowercase-handle convention.
- **Manifest folder path** under `manifests/` in `microsoft/winget-pkgs`:
  `manifests/a/AlastairLundy/HarnessGateway/<version>/`
- **Files in that folder**: the version manifest `<identifier>.yaml`, the installer manifest `<identifier>.installer.yaml`, and the locale manifest `<identifier>.locale.en-US.yaml`, where `<identifier>` is the PackageIdentifier above.

Commands in this runbook write the identifier as `PACKAGE_IDENTIFIER`. Substitute the value above. `<version>` is the published tag itself: tags are unprefixed and equal the project version, so tag `0.1.0` means manifest version `0.1.0`. The dispatch workflow asserts the tag matches the csproj `<Version>`. The repository slug `alastairlundy/HarnessGateway` stays lowercase and does not affect the identifier's casing.

## One-time setup

- Install Komac: `winget install komac`.
- Create a **classic** GitHub token with the `public_repo` scope and store it on your machine with `komac token update`. If GitHub CLI already holds a suitable token, run `komac token update --token=$(gh auth token)` instead.
- Keep the WinGet client installed for `winget validate`.

See [Token posture](#token-posture) for why the token must be classic and the rules that keep it out of CI.

## Per-release steps

Release notes are written by hand — generated changelogs are disabled. Before dispatching `Publish release`, create and push the tag and open the draft yourself:

```bash
git tag 0.1.0 <HEAD-sha>
git push origin 0.1.0
gh release create 0.1.0 --repo alastairlundy/HarnessGateway --draft \
  --title 0.1.0 --target <HEAD-sha> --notes "<your release notes>"
```

The dispatch workflow requires that draft to exist: it attaches the built binaries to it and publishes it, leaving the hand-written body untouched. Keep the tag unprefixed (`0.1.0`), matching the csproj `<Version>`.

### 1. Confirm the published assets

When the `Publish release` dispatch workflow has flipped the draft release to published, confirm both MSIs are attached:

```bash
gh release view <tag> --repo alastairlundy/HarnessGateway --json assets --jq '.assets[].name'
```

The manifest references the asset URLs that dispatch produced. Their shape is:

```text
https://github.com/alastairlundy/HarnessGateway/releases/download/<tag>/harnessgateway-win-x64.msi
https://github.com/alastairlundy/HarnessGateway/releases/download/<tag>/harnessgateway-win-arm64.msi
```

If either MSI is missing, stop. There is nothing to submit for this version.

### 2. Generate the manifest files with Komac

Set the published release's base URL, download both MSIs, and compute their
SHA-256 hashes for the cross-check below:

```powershell
# Example for tag 0.1.0; swap in the tag you just published.
$base = "https://github.com/alastairlundy/HarnessGateway/releases/download/0.1.0"
Invoke-WebRequest "$base/harnessgateway-win-x64.msi" -OutFile harnessgateway-win-x64.msi
Invoke-WebRequest "$base/harnessgateway-win-arm64.msi" -OutFile harnessgateway-win-arm64.msi
Get-FileHash .\harnessgateway-win-x64.msi, .\harnessgateway-win-arm64.msi -Algorithm SHA256
```

Generate the manifest files from those two published URLs. Substitute the PackageIdentifier from Package identity for `PACKAGE_IDENTIFIER`; the version is the tag itself, so use `0.1.0`:

```powershell
komac update PACKAGE_IDENTIFIER --version 0.1.0 --urls "$base/harnessgateway-win-x64.msi" "$base/harnessgateway-win-arm64.msi" --output .\winget-out
```

Komac downloads both installers from the URLs, analyzes them, and records each installer's URL and SHA-256 in the generated files. At the final prompt choose **Exit**, which writes the files and opens no PR. Newer Komac versions also accept `--exit` to skip the prompt. Create the package's very first version with `komac new` instead. The URL, hash, and content rules below apply to it the same way.

Then cross-check the output: every `InstallerSha256` in the generated installer manifest must equal the `Get-FileHash` value for the same architecture's MSI, and every `InstallerUrl` must be one of the two published URLs.

The files land in
`.\winget-out\manifests\a\AlastairLundy\HarnessGateway\0.1.0\`.

### 3. Check the manifest content rules

| Rule | Value |
|------|-------|
| `PackageIdentifier` | The canonical identifier from [Package identity](#package-identity), verbatim |
| `InstallerType` | `wix`. The installer is a WiX-built MSI |
| `Scope` | `user`. Manifest installs and upgrades never trigger UAC. The MSI's manual default stays per-machine, and that divergence is deliberate |
| Installer entries | One entry per architecture: `x64` for win-x64 and `arm64` for win-arm64, each pointing at its own published MSI URL with its SHA-256 |
| `InstallerSwitches` | Silent-switch entries match the switches the MSI declares: `/quiet` with `/norestart` for silent operation, and the per-user override `ALLUSERS=2 MSIINSTALLPERUSER=1` exactly as documented in the README's MSI section. Never invent a switch the MSI does not declare |

### 4. Validate locally

```powershell
winget validate .\winget-out\manifests\a\AlastairLundy\HarnessGateway\0.1.0
```

`winget validate` is the local pre-submission gate. The `microsoft/winget-pkgs` validation pipeline remains the authoritative acceptance gate. A manifest that passes locally can still fail upstream.

### 5. Open the PR for this version

Submit exactly the files that passed validation:

```powershell
komac submit .\winget-out --yes
```

- Open the PR right after dispatch publishes the release, in the same working window. One PR per release for that version, never batched across releases.
- The PR is maintainer-owned. No CI credential may write to `microsoft/winget-pkgs`.
- Watch the upstream validation pipeline on the PR and fix any failure in that same PR.

## Token posture

- Komac needs a **classic** GitHub token with the `public_repo` scope to open the PR. Fine-grained tokens can create the manifests but cannot open the
  pull request, per
  [Komac issue #310](https://github.com/russellbanks/Komac/issues/310).
- The token stays with the maintainer. Store it with `komac token add` on your machine, or pass it locally through `GITHUB_TOKEN`/`--token`. It never enters CI. No workflow file, repository secret, or Actions environment carries it.
- Manifest PR authorship is maintainer-owned. No CI credential may write to `microsoft/winget-pkgs`. This PR is the single third-party write the packaging carve-out in `docs/agents/out-of-scope.md` permits.
