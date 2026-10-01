# Contributing to HarnessGateway

Thanks for taking the time to contribute. This guide is for code contributors. It gets you from a fresh clone to a pull request that is ready for review. If you only want to report a problem or suggest an improvement, use the issue templates. They ask for exactly the information we need.

Two documents own this repository's conventions. This guide links to them instead of repeating them, so the conventions can only drift in one file:

- [`AGENTS.md`](AGENTS.md) covers codebase structure, architecture, style rules, test gotchas, and the commands the maintainers actually run.
- [`docs/agents/out-of-scope.md`](docs/agents/out-of-scope.md) is the standing "we deliberately don't do this" list. Read it before proposing a new flag, verb, or behavior. Anything listed there needs a new decision first, not a pull request.

By participating you agree to the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).

## Development environment

You need git and the .NET SDK pinned by [`global.json`](global.json) (currently 10.0.401 with `rollForward: latestFeature`). Check the SDK with `dotnet --version`.

Nothing else. The test suite runs on fakes, so no agent harnesses, no API keys, and no network access are required to build the project or run the tests.

## Build

The repository has exactly one solution, [`src/HarnessGateway.slnx`](src/HarnessGateway.slnx). There is no repo-root `.sln`, so always point the CLI at the slnx file:

```bash
dotnet build src/HarnessGateway.slnx
```

## Test

```bash
dotnet test --solution src/HarnessGateway.slnx
```

Tests run on the native Microsoft Testing Platform, with the runner pinned by the `test.runner` entry in `global.json`. TUnit is the test framework, not xUnit or NUnit, and assertions are written `await Assert.That(...)`.

### Filtering to one class or test

TUnit filters use `--treenode-filter`, whose value is a `/`-separated path through the test tree: `/<assembly>/<namespace>/<class>/<test>`, with `*` matching anything at a level.

```bash
# Run a single test class
dotnet test --solution src/HarnessGateway.slnx --no-build --treenode-filter "/*/*/SkeletonTests/*"
```

Two gotchas that cost people time here:

- There is no `--` bridge separator. That is VSTest-mode syntax and it fails in this repository.
- `--filter-class` does not work on this setup; it fails with MTP exit code 5. Use `--treenode-filter`.

### What CI runs

Every push to `main` and every pull request runs [`.github/workflows/ci.yml`](.github/workflows/ci.yml) on a single Linux runner: a Release build, the full test suite, and a gate that fails on any new Meziantou analyzer warning. The known baseline is inline in the workflow file. Reproduce the gate locally:

```bash
dotnet restore src/HarnessGateway.slnx
dotnet build src/HarnessGateway.slnx --configuration Release --no-restore
dotnet test --solution src/HarnessGateway.slnx --configuration Release --no-build
```

New analyzer warnings get fixed, not baselined.

## Package versions (CPM)

Versions are managed with Central Package Management. Every package version lives in [`src/Directory.Packages.props`](src/Directory.Packages.props) as a `<PackageVersion>` item:

- Never put `Version=` on a `PackageReference` in a project file. Projects reference packages without versions, and the central file decides the version.
- To add a dependency, add `<PackageVersion Include="Package.Id" Version="x.y.z" />` to the central file first, then reference the package version-free.
- To upgrade or downgrade a package, change the single `PackageVersion` entry. Every project in the solution then resolves the same version.

## Opening a pull request

PRs target `main`. The pull request template walks you through the details. The short version:

- **Tests.** Behavior changes ship with tests (TUnit, fakes-only, nothing networked in the main suite). The full suite must pass: `dotnet test --solution src/HarnessGateway.slnx`.
- **Exit codes.** The exit-code table in the README is an append-only contract. Never renumber or repurpose an existing code, and add a row to the README's Exit codes table when you claim a new one.
- **README.** If the change affects anything a user can see or type (a flag, verb, config key, artifact, or documented behavior), update the README in the same PR. The README must match the shipped CLI surface.
- **Scope and style.** Keep the change consistent with the file you are editing, and check `docs/agents/out-of-scope.md` before widening scope with a new flag or verb.
- **AI-usage disclosure.** The PR template requires it, with exactly two options: no AI usage, or AI usage described with the tool or model and what it produced.

Smaller, focused PRs review faster than one that mixes refactors with behavior changes.

---

Release process, versioning policy, and maintainer duties are deliberately not covered by this document.
