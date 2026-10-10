# Decision Ledger — HarnessGateway release ship shape

Session: compile/ship shape (AoT vs trim vs baseline) + NuGet/dotnet-tool distribution re-examination.
Baseline artifacts: `dotnet-releaser.toml` §msbuild.properties (self-contained, multi-file, no trim/R2R/AoT), `docs/agents/out-of-scope.md` §Packaging (NuGet/dotnet-tool standing refusal — re-opened this session; dated ledger deleted, this session's records supersede where they conflict).

### [D001] - session goal

- **Driver**: The maintainer wants the distributed launcher's compile/ship shape and channels to match actual usage: short-lived invocations (`launch`/`config`) that spawn heavier harness processes, refreshed by downloading new release archives.
- **Resolved Answer**: "decide the next release's compile/ship shape (Native AoT vs trim vs baseline) and settle whether NuGet/dotnet-tool shipping returns" — both in session (user picked "Both").
- **Normalized Requirement**: The session shall resolve publish shape and distribution channel for the next release, and produce the change plan (csproj, `dotnet-releaser.toml`, CI, `docs/agents/out-of-scope.md`) for the user to approve before implementation.
- **Constraints**: track: implementation (decide then apply); ledger persists post-session by default; no glossary term expected unless a concept earns one.

### [T001] - publish shape

- **Driver**: Big per-RID archive size is today's largest distribution cost, and startup on a launcher spends JIT time before it can do its real work (spawning a harness process); the maintainer prefers additive optimizations with working reflection semantics over the AoT migration cost that corrected evidence scoped (per-OS CI matrix + STJ source-gen conversion).
- **Resolved Answer**: "Publish shape is R2R with Trimming" — `PublishReadyToRun=true` + `PublishTrimmed=true`, self-contained stays on, `PublishAot` stays off.
- **Normalized Requirement**: Release publishes for all six RIDs shall be self-contained with IL trimming and ReadyToRun pre-compilation enabled; `PublishAot` remains false.
- **Constraints**: Trim mode (partial vs full) deferred to T005; single-file bundling deferred to T006 (`PublishSingleFile` stays false until then); the stale "trimming is unsafe" rationale in `dotnet-releaser.toml` lines 36–38 gets rewritten to match the corrected evidence; Supersedes the toml's prior deliberate-off posture for trim/R2R only.
- **Cites**: D001

### [T002] - distribution channel

- **Driver**: The archives already cover every user group the tool has; adding a NuGet/dotnet-tool path was speculative maintenance weight for a maintainer-frequented pipeline.
- **Resolved Answer**: "Archives only but using the updated publish shape from T001" — per-OS zip/tar.gz archives (Windows), tar.gz + unsigned .deb/.rpm (Linux), tar.gz (macOS); no .nupkg is produced or published to any feed.
- **Normalized Requirement**: Releases shall ship only the per-RID archive artifacts described in `dotnet-releaser.toml`, built with the T001 publish shape; `dotnet-releaser.toml` §nuget `publish = false` remains; `docs/agents/out-of-scope.md` §Packaging refusal is updated in place with the fresh rationale (channels reconsidered and declined this session).
- **Constraints**: Package-manager channels (brew/scoop) refusals stand; signing refusal stands; the re-open lever remains an explicit future decision; a future dotnet-tool path would be a new record citing this one.
- **Cites**: D001, T001

### [T003] - CI analyzer gate

- **Driver**: The maintainer wants compile-shape regressions (reflection creeping back in, trim-incompatible dependency upgrades) surfaced by the same gate discipline the repo already uses for Meziantou warnings, without paying `IsAotCompatible`'s package-level implications on non-AoT publishes.
- **Resolved Answer**: "Add EnableTrimAnalyzer and IsTrimmable + EnableAoTAnalyzer" — set `IsTrimmable` (Launcher Core at minimum, Launcher exe included) and turn on both analyzers with trim/AoT IL warnings treated like the Meziantou gate, using the inline-baseline pattern for pre-existing warnings.
- **Normalized Requirement**: CI shall compile the solution with the trim and AoT analyzers enabled and fail on new IL trim/AoT warnings not present in the committed inline baseline, mirroring the existing new-Meziantou-warnings gate.
- **Constraints**: The gate does not itself set `PublishTrimmed`/`PublishAot`; analyzer findings on dependency internals are handled via baseline entries or suppression, not ignored notifications; gate shape defined against `.github/workflows/ci.yml`'s existing inline-baseline precedent.
- **Cites**: D001, T001

### [T004] - replace the reflection JSON surface

- **Driver**: The T003 gate should flag new reflection regressions, not carry self-inflicted ones; the serialized file contracts stay untouched.
- **Resolved Answer**: "Option A — source-gen contexts replace all three call sites."
- **Normalized Requirement**: Core shall replace the reflection-based `JsonSerializer` calls in `BackupService.cs`, `SettingsStore.cs`, and `DpapiSecureKeyStore.cs` with a source-generated `JsonSerializerContext` covering `BackupRecord`, `LauncherSettings`, and `Dictionary<string,string>`, preserving the current runtime options (`WriteIndented` as written today); the serialized file formats shall not change.
- **Constraints**: Hybrid/reflection-fallback architectures declined (T004's Option C rejected); test expectations for the round-trip outputs remain the contract check; analyzer gate (T003) becomes the regression net for the converted call sites.
- **Cites**: D001, T003

### [T005] - trim mode

- **Driver**: The maintainer wants the real archive shrink without betting blind — the observable (analyzer baseline tier) should decide whether to keep or re-score the mode before release.
- **Resolved Answer**: "Option C — full now, step back to partial only if dependency warnings dominate."
- **Normalized Requirement**: `TrimMode=full` shall be pinned explicitly in `dotnet-releaser.toml` §msbuild.properties for the six-RID publish; if the T003 analyzer baseline shows dependency-internal warnings dominating the tier, the mode shall be reconsidered (partial) before the release ships.
- **Constraints**: The threshold rule ("what counts as dominating") must be written into the implementation blueprint, not left to the implementer's hindsight; T003's gate owns the observable.
- **Cites**: D001, T001, T003

<!-- next-d: D002 -->
<!-- next-t: T006 -->
