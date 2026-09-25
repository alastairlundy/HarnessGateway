---
title: Solution and project scaffolding
classification: Independent
blocked_by: []
parent: IMPLEMENTATION-HarnessLauncher.md
---

## Goal

Add the two new projects to the solution and wire every package reference up front, so no later ticket needs to edit a project file for dependencies. This establishes the Core + app + tests shape the future unified CLI inherits.

## What to build

Add `LmProxy.AgentLauncher.Core` (class library, net10.0) and `LmProxy.AgentLauncher.Tests` (test project) to `src/LmProxy.slnx`. The app project `src/LmProxy.AgentLauncher` already exists with the locked package stack referenced (System.CommandLine 2.0.12, CliInvoke 3.0.0-beta.3, Microsoft.Extensions.Configuration.Json and Microsoft.Extensions.Configuration.EnvironmentVariables 10.0.12).

- Core references CliInvoke 3.0.0-beta.3 and the two Microsoft.Extensions.Configuration packages. Spectre.Console must NOT be referenced in Core - console UI stays in the app project.
- The app project adds Spectre.Console for rendering and prompts; command parsing stays System.CommandLine.
- The test project uses TUnit and references both Core and the app project.
- Code is organized by feature folders as tickets add files - Core grows `Backends/`, `Settings/`, `Adapters/`, `Configuration/`, `Execution/`, `Launch/`; the app grows `Commands/` and `Wizard/`. No folder stubs are required at scaffold time.

## Size

- **Files** - 4 (edit `src/LmProxy.slnx`, edit `src/LmProxy.AgentLauncher/LmProxy.AgentLauncher.csproj`, create `src/LmProxy.AgentLauncher.Core/LmProxy.AgentLauncher.Core.csproj`, create `src/LmProxy.AgentLauncher.Tests/LmProxy.AgentLauncher.Tests.csproj`)

## Recommended Workflow

### Step 1 - Add the Core class library

Where: `src/LmProxy.AgentLauncher.Core/LmProxy.AgentLauncher.Core.csproj`, `src/LmProxy.slnx`

- Create the class library project targeting net10.0 with ImplicitUsings and Nullable enabled.
- Reference CliInvoke 3.0.0-beta.3, Microsoft.Extensions.Configuration.Json 10.0.12, Microsoft.Extensions.Configuration.EnvironmentVariables 10.0.12.
- Add the project to `src/LmProxy.slnx`.

Verify: `dotnet build src/LmProxy.slnx` restores and builds the new project.

### Step 2 - Add the TUnit test project

Where: `src/LmProxy.AgentLauncher.Tests/LmProxy.AgentLauncher.Tests.csproj`, `src/LmProxy.slnx`

- Create the test project with the TUnit packages and project references to Core and the app project.
- Add the project to the solution.

Verify: a skeleton test runs green via the TUnit runner.

### Step 3 - Add Spectre.Console to the app project

Where: `src/LmProxy.AgentLauncher/LmProxy.AgentLauncher.csproj`

- Add the Spectre.Console package reference alongside the existing locked packages.

Verify: restore succeeds with no version conflicts.

### Step 4 - Verify the solution builds clean

Where: N/A

- Run a full solution build and the (skeleton) test suite.

Verify: zero build errors; the test runner exits successfully.

## Context pointers

##### Files

- `src/LmProxy.slnx` - solution to extend
- `src/LmProxy.AgentLauncher/LmProxy.AgentLauncher.csproj` - carries the locked package stack; add Spectre.Console here
- `src/LmProxy.AgentLauncher/Program.cs` - placeholder; replaced by the direct-launch-path ticket

##### ADRs

None - the repository has no `docs/adr/` directory.

##### Domain terms

- Adapter - the Core/app split keeps adapters testable headlessly
- Harness - the launcher's subject domain

##### Ledger records

- `DECISIONS-LmProxy-agent-launcher.md#D002` - net10.0 console app under `src/LmProxy.AgentLauncher`
- `DECISIONS-LmProxy-agent-launcher.md#D003` - locked package stack (System.CommandLine, CliInvoke, Microsoft.Extensions.Configuration)
- `DECISIONS-LmProxy-agent-launcher.md#T001` - feature-folder organization
- `DECISIONS-LmProxy-agent-launcher.md#T002` - Core + CLI + tests solution shape
- `DECISIONS-LmProxy-agent-launcher.md#T004` - Spectre.Console in the app project only; parsing stays System.CommandLine
- `DECISIONS-LmProxy-agent-launcher.md#T009` - TUnit with the fakes strategy

## Acceptance criteria

- [ ] `src/LmProxy.slnx` contains LmProxy.AgentLauncher.Core, LmProxy.AgentLauncher, and LmProxy.AgentLauncher.Tests; `dotnet build src/LmProxy.slnx` succeeds
- [ ] Core references exactly CliInvoke 3.0.0-beta.3 and Microsoft.Extensions.Configuration.Json + .EnvironmentVariables 10.0.12, and does not reference Spectre.Console
- [ ] The app project references System.CommandLine 2.0.12, CliInvoke 3.0.0-beta.3, both Microsoft.Extensions.Configuration packages, and Spectre.Console
- [ ] The test project uses TUnit and references Core and the app project
- [ ] No later ticket needs to edit a project file for package references (packaging metadata in the packaging ticket excepted)

## Dependencies

**Blocked by** - None - can start immediately
