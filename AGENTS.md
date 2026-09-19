# AGENTS.md — HarnessGateway

CLI (`harnessgateway`) that launches agent harnesses (OpenCode, Kilo, Copilot CLI, Pi) pointed at a chosen backend (OpenAI/Anthropic-compatible endpoint). A future Proxy API (`HarnessGateway.Proxy`) is planned; the launcher currently owns the whole codebase — don't invent a Proxy project yet.

## Build & test

- Solution is `src/HarnessGateway.slnx` — use `--solution`, not a repo-root `.sln`.
- Tests run on **native Microsoft Testing.Platform** (`global.json` pins `test.runner`):

  ```bash
  dotnet test --solution src/HarnessGateway.slnx
  ```

  No `--` bridge separator, no positional project path — those are VSTest-mode syntaxes and will fail here.
- Framework is **TUnit** (not xUnit/NUnit): `Assert.That(...)` style, MTP filters (`--filter-class` etc. directly, no `--`). Plain constant values inside `Assert.That` are analyzer errors.
- Package versions come from CPM (`src/Directory.Packages.props`) — never add `Version=` on a `PackageReference` there; use the tool or `NuGet.NuGet` context for upgrades.

## Structure

- `HarnessGateway.Launcher` — `OutputType=Exe`, the dotnet tool (`PackageId=harnessgateway`, `ToolCommandName=harnessgateway`). Console entry: `Program.cs` + `Commands/`.
- `HarnessGateway.Launcher.Core` — domain logic (Adapters, Backends, Settings, Launch). Referenced by the exe; both are `InternalsVisibleTo` the tests.
- Tests use `InternalsVisibleTo` — test against internal types when the public surface is too thin, rather than weakening kinds in Core.
- Per-harness behavior lives in `Core/Adapters/*Adapter.cs` one class per harness: detection, config write, launch. Adding a harness = adding an adapter + `HarnessKind` entry + tests.

## Conventions & gotchas

- **Exit codes are a documented contract** (README table, enum in `Commands/LaunchExitCode.cs`). Don't renumber or add without updating README.
- Settings file is `HarnessGateway-launcher.json` in the per-OS config dir (`%APPDATA%\HarnessGateway` / `~/Library/Application Support/HarnessGateway`). `ApiKey` is currently stored in plaintext — hardcoded TODO for keychain/DPAPI; don't make it worse.
- Schema-strict history: explicit types over `var`, file-scoped namespaces. Match neighbors.
- Repo was renamed from LmProxy → HarnessGateway (system/repo rename); legacy names may linger in release tags, and TODO.md still cites `docs/decisions/DECISIONS-LmProxy-agent-launcher.md` — but `docs/` is currently empty, so the decision ledger is missing; treat those citation IDs as unresolvable prose, not files.
- The future plan, when the Proxy API gets verbs (per TODO `#D017`), is a unified CLI rename/restructure — keep new commands lean so the restructure is cheap.
- Release artifacts are per-OS archives named `harnessgateway-<rid>.zip/.tar.gz`; update `.github/workflows/release.yml` if the project or tool name changes again.
