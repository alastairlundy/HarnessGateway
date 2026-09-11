# Decision Ledger — LmProxy AgentLauncher

Session: technical-grilling — AgentLauncher CLI design (track: concept-then-implementation)
Created: 2026-09-09

### [D001] - session goal

- **Driver**: Launch AI agent harnesses locally without manual per-harness configuration, the way `ollama launch` does — but backend-agnostic (OpenAI-compatible or Anthropic-compatible).
- **Resolved Answer**: Build a .NET console CLI with two entrypoints — `launch` (no args: TUI wizard listing supported harnesses, opening the choice in the current working directory) and `launch <agent>` (applies the required config actions, then launches the harness in the current working directory) — supporting OpenCode, Kilo, GitHub Copilot CLI, and Pi in v1.
- **Normalized Requirement**: The CLI shall expose `launch` and `launch <agent>`; both shall configure the selected harness for the chosen backend and start it in the current working directory.
- **Constraints**: v1 harness scope = OpenCode, Kilo, GitHub Copilot CLI, Pi; Codex, Claude Code, OpenClaw, Hermes, Cline deferred to a later version. Backend type is user-selected: OpenAI-compatible or Anthropic-compatible. Goal-record draft was presented verbatim pre-session; user confirmed all scope items locked without revision.

### [D002] - locked: .NET console app on net10.0

- **Driver**: AgentLauncher is part of the LmProxy .NET solution and builds on the owner's existing .NET tooling.
- **Resolved Answer**: Resolved (user-confirmed locked) — Console app under `src/LmProxy.AgentLauncher` targeting net10.0.
- **Normalized Requirement**: The program is a .NET 10 console application in project `src/LmProxy.AgentLauncher`.
- **Constraints**: Confirmed locked via Gate A intake (user selected item A as locked).

### [D003] - locked: csproj stack

- **Driver**: Packages already referenced in the project scaffold reflect the owner's preferred libraries (CliInvoke is the owner's own library).
- **Resolved Answer**: Resolved (user-confirmed locked) — System.CommandLine 2.0.12, CliInvoke 3.0.0-beta.3, Microsoft.Extensions.Configuration.Json + .EnvironmentVariables 10.0.12.
- **Normalized Requirement**: The CLI uses System.CommandLine for command parsing, CliInvoke for external process invocation, and Microsoft.Extensions.Configuration for settings.
- **Constraints**: No TUI package is referenced yet — the wizard TUI library is NOT covered by this lock; confirmed locked via Gate A intake.

### [D004] - locked: two entrypoints

- **Driver**: Mirror `ollama launch` ergonomics — no-args interactive pick, or direct invoke by name.
- **Resolved Answer**: Resolved (user-confirmed locked) — `launch` with no args opens a TUI wizard; `launch <agent-harness-name>` applies config actions and launches directly; both act on the current working directory.
- **Normalized Requirement**: `launch` (no args) presents a harness-selection wizard and opens the chosen harness in the CWD; `launch <name>` configures and launches the named harness in the CWD.
- **Constraints**: Confirmed locked via Gate A intake.

### [D005] - locked: v1 harness set

- **Driver**: Cover the harnesses the owner actually uses first.
- **Resolved Answer**: Resolved (user-confirmed locked) — v1 supports OpenCode, Kilo, GitHub Copilot CLI, and Pi.
- **Normalized Requirement**: Version 1 ships support for exactly OpenCode, Kilo, GitHub Copilot CLI, and Pi.
- **Constraints**: Confirmed locked via Gate A intake.

### [D006] - locked: backend choice (protocol flavors)

- **Driver**: Ollama's launcher hardwires Ollama; this tool must not hardwire a single protocol.
- **Resolved Answer**: Resolved (user-confirmed locked) — the user specifies the backend as either an OpenAI-compatible or an Anthropic-compatible backend.
- **Normalized Requirement**: Every launch flow shall accept a backend specification whose protocol flavor is OpenAI-compatible or Anthropic-compatible.
- **Constraints**: What a "backend" concretely is (URL? product? LmProxy itself?) is open — see D008/D009; confirmed locked via Gate A intake.

### [D007] - locked: deferred harnesses

- **Driver**: Ship v1 without blocking on the full harness catalog.
- **Resolved Answer**: Resolved (user-confirmed locked) — Codex, Claude Code, OpenClaw, Hermes, and Cline support comes in a future version.
- **Normalized Requirement**: v1 shall not be blocked on support for Codex, Claude Code, OpenClaw, Hermes, or Cline; later versions may add them.
- **Constraints**: v1 design must not foreclose adding them (see support-contract branch); confirmed locked via Gate A intake.

### [I001] - backend definition

- **Prompt**: For D008 – Backend definition: pick an option, hybridize, or provide your own answer.
- **User Response**: "Option A but there should 3 variants: OpenAI Chat Completions, OpenAI Responses, and Anthropic compatible."
- **Resolution**: D008 resolved as a protocol endpoint (base URL + flavor) with a three-flavor protocol enum instead of two; refines D006's OpenAI-compatible wording into two OpenAI-family variants.
- **Notes**: Glossary term "Backend" proposed after resolution.

### [I002] - backend target scope

- **Prompt**: For D009 – Backend target scope: pick an option, hybridize, or provide your own answer.
- **User Response**: "LmProxy itself (distinct from this launcher) doesn't currently exist so I'm inclined to say User should be able to set/configure the local endpoint to avoid needing to repeat it each time, but that the user should be able to use the flag/argument to override the local setting. This may call for a config command to easily enable modifying the default endpoint."
- **Resolution**: D009 resolved as a user-configurable default endpoint (saved setting) with per-launch flag/argument override; no built-in product default since LmProxy-the-proxy does not exist yet; a `config` command is anticipated and will be confirmed at the command-surface branch.
- **Notes**: LmProxy-the-proxy is a separate, not-yet-built product — the launcher must not assume it exists or hardwire its endpoint.

### [I003] - config action meaning

- **Prompt**: For D010 – Config action meaning: pick an option, hybridize, or provide your own answer.
- **User Response**: "D010: Option C"
- **Resolution**: D010 resolved as hybrid per harness: env vars/flags where supported, minimal backed-up config-file writes where required.
- **Notes**: Persistence and cleanup semantics deferred to D011; glossary term "Configuration action" proposed after resolution.

### [D008] - Backend definition

- **Driver**: Nothing about an endpoint should be hardwired by product name; the wire protocol is what the launcher actually must configure for.
- **Resolved Answer**: Option A, refined — a backend is a base URL plus one of three protocol flavors: OpenAI Chat Completions, OpenAI Responses, or Anthropic-compatible.
- **Normalized Requirement**: A backend is identified by a base URL and a protocol flavor drawn from exactly: OpenAI Chat Completions, OpenAI Responses, Anthropic-compatible.
- **Constraints**: Refines D006 (OpenAI-compatible splits into Chat Completions and Responses; Anthropic remains one flavor). No product-name registry — known products may later prefill URL/flavor without changing this model. Glossary term "Backend" accepted and written to GLOSSARY.md (definition mirrors this record's Normalized Requirement).

### [D009] - Backend target scope

- **Driver**: Avoid repeating endpoint entry on every launch while keeping per-launch flexibility.
- **Resolved Answer**: User-configurable default endpoint (a saved local setting used automatically), overridable per launch via flag/argument; a `config` command may be added to edit the default. (LmProxy-the-proxy does not exist yet, so there is no built-in product default.)
- **Normalized Requirement**: The launcher shall store a user-configurable default endpoint; `launch` uses it unless a per-launch flag/argument overrides it.
- **Constraints**: No built-in product endpoint (LmProxy does not exist yet). The `config` command was confirmed in scope at D016. Saved default includes its protocol flavor (D008). Glossary term "Default endpoint" accepted and written to GLOSSARY.md.

### [D010] - Config action meaning

- **Driver**: Support each of the four v1 harnesses via its natural configuration channel rather than forcing one mechanism on all.
- **Resolved Answer**: Option C — hybrid per harness: env vars and CLI flags where the harness supports them; minimal config-file writes (backed up) where required.
- **Normalized Requirement**: Each harness adapter shall configure the chosen backend via env vars/flags when supported and via minimal backed-up config-file writes otherwise.
- **Constraints**: Persistence, backup, and cleanup semantics for file writes are decided in D011. Glossary term "Configuration action" accepted and written to GLOSSARY.md.

### [I004] - config persistence and cleanup

- **Prompt**: For D011 – Config persistence and cleanup: pick an option, hybridize, or provide your own answer.
- **User Response**: "Option A - I don't want to cause lasting damage/changes to the user's existing setup by default. An argument or flag can be added to allow users to persist if they want."
- **Resolution**: D011 resolved as restore-on-exit by default (Option A) with an opt-in persist flag (absorbing B's revert path as a flag rather than a command).
- **Notes**: Depends on D015 lifecycle for exit detection; crashed harness may leak writes until next launch or manual restore — the D010 backup enables recovery. Persist flag naming lands at D016.

### [I005] - credentials and API keys

- **Prompt**: For D012 – Credentials and API keys: pick an option, hybridize, or provide your own answer.
- **User Response**: "D012: Option C"
- **Resolution**: D012 resolved as optional pass-through: saved key injected when present; keyless local endpoints launch without one; dummy value only where a harness rejects empty keys.
- **Notes**: Per-harness dummy-key behavior documented in adapters; key storage format/security deferred to Phase 2.

### [I006] - model selection

- **Prompt**: For D013 – Model selection: pick an option, hybridize, or provide your own answer.
- **User Response**: "Option B but if the user omits the model then if they use the interactive wizard it should prompt them to choose one from the local endpoint's declared models support (endpoint APIs typically have a /models type of endpoint that lists all supported models) or let them skip. The problem is I don't know what should happen if the user doesn't provide one if they skip (wizard) or if they don't provide one on the non-wizard path since different harnesses have different behaviours such as not functioning/throwing an error because they require a model to be specified, calling the /models type endpoint to discover available models itself, or other behaviour."
- **Resolution**: D013 resolved as optional model per launch (B) plus a wizard model step fed by the backend's models-listing endpoint with an explicit skip option; the unresolved skip semantics became new branch D014.
- **Notes**: Wizard model step depends on endpoint reachability and must degrade gracefully when unreachable; both OpenAI and Anthropic protocols expose models-listing endpoints.

### [D011] - Config persistence and cleanup

- **Driver**: Launch must not cause lasting damage or changes to the user's existing setup by default.
- **Resolved Answer**: Option A — restore-on-exit by default, with a flag/argument that lets users opt in to persisting the written config.
- **Normalized Requirement**: Config-file writes shall be automatically restored to their original state when the harness exits, unless an explicit persist flag is given; originals are backed up before the first write.
- **Constraints**: Requires exit detection — interplay with D015 lifecycle. A crashed harness may leak writes until next launch or manual restore (D010 backup enables recovery). Persist-flag naming confirmed at D016.

### [D012] - Credentials and API keys

- **Driver**: Cover keyed cloud backends and keyless local backends without making secret storage mandatory.
- **Resolved Answer**: Option C — inject a key only if the user saved one for the backend; keyless local endpoints launch without one; a dummy value is supplied only where a harness rejects empty keys.
- **Normalized Requirement**: The launcher shall pass saved credentials to the harness when present, launch without credentials when the backend needs none, and supply a dummy value only where the harness rejects empty keys.
- **Constraints**: Per-harness dummy-key behavior must be documented in each adapter; credential storage format and security posture are Phase 2 decisions.

### [D013] - Model selection

- **Driver**: Match the `ollama launch` wizard feel while keeping direct launches frictionless.
- **Resolved Answer**: Option B with a wizard enhancement — model is optional; the interactive wizard prompts the user to pick from the backend's declared models (models-listing endpoint, e.g. `/v1/models`) or skip; `--model` sets it in direct mode.
- **Normalized Requirement**: Launch shall accept an optional model; the wizard shall offer a model picker populated from the backend's models-listing endpoint with an explicit skip option; direct mode accepts a model flag.
- **Constraints**: Skip semantics are unresolved — new branch D014. The wizard model step depends on endpoint reachability and must degrade gracefully when the endpoint is unreachable. Model IDs are not validated beyond catalog membership.

### [I007] - model skip semantics

- **Prompt**: For D014 – Model skip semantics: pick an option, hybridize, or provide your own answer.
- **User Response**: "D014: Option A"
- **Resolution**: D014 resolved as adapter-declared model requirements: adapters declare whether their harness needs a model; wizard blocks skip for those, allows skip otherwise; direct mode warns when a required model is missing.
- **Notes**: Behavior for future harnesses lacking a declaration leans warn-and-launch — to confirm in the Phase 2 adapter spec.

### [I008] - process lifecycle

- **Prompt**: For D015 – Process lifecycle: pick an option, hybridize, or provide your own answer.
- **User Response**: "D015: Option A - This is how ollama launch currently works."
- **Resolution**: D015 resolved as block-and-inherit foreground execution; launcher exits with the harness's exit code; makes D011's restore-on-exit reliable.
- **Notes**: User explicitly anchored to ollama launch's current behavior.

### [I009] - command surface and naming

- **Prompt**: For D016 – Command surface and naming: pick an option, hybridize, or provide your own answer.
- **User Response**: "D016: Option B"
- **Resolution**: D016 resolved as a named root command with `launch`, `launch <agent>`, and `config` subcommands; root name unresolved at this point and moved to D017.
- **Notes**: `config` subcommand closes D009's pending constraint.

### [D014] - Model skip semantics

- **Driver**: No launcher-invented failures; behavior must match what each harness actually does.
- **Resolved Answer**: Option A — adapter-declared requirement.
- **Normalized Requirement**: Each adapter shall declare whether its harness requires an explicit model; the wizard blocks model-skip for harnesses that do and allows skip otherwise; direct mode warns when a required model was not provided.
- **Constraints**: Adapter metadata must be kept accurate as harnesses evolve. Behavior for harnesses lacking a declaration (future ones) leans warn-and-launch — confirm in the Phase 2 adapter spec.

### [D015] - Process lifecycle

- **Driver**: `ollama launch` runs the harness in the foreground today; matching that keeps launch predictable.
- **Resolved Answer**: Option A — block and inherit: foreground execution with inherited stdio; launcher exits with the harness's exit code. (User: "This is how ollama launch currently works.")
- **Normalized Requirement**: Launch shall run the harness in the foreground with inherited stdio and exit with the harness's exit code.
- **Constraints**: Enables D011's restore-on-exit guarantee; interrupt handling (Ctrl+C) must still restore config — Phase 2 detail.

### [D016] - Command surface and naming

- **Driver**: Give `config` a home and reserve room for the future LmProxy CLI without changing the locked entrypoints.
- **Resolved Answer**: Option B — named root command with `launch`, `launch <agent>`, and `config` subcommands.
- **Normalized Requirement**: The CLI shall expose a named root command whose subcommands include `launch`, `launch <agent>`, and `config`, preserving D004's entrypoint behavior.
- **Constraints**: Root name and CLI ownership resolved at D017.

### [D017] - CLI ownership and root name

- **Driver**: One install, one brand, one settings store — the launcher and the future proxy should not compete for command names.
- **Resolved Answer**: Unified CLI confirmed — one CLI project eventually owns both the agent harness launcher and the proxy server. Root command name DEFERRED (user: "Decide later").
- **Normalized Requirement**: A single CLI project shall own `launch`/`config` today and the proxy-server verbs later, with a shared settings store; the root command name must be decided before proxy verbs ship.
- **Constraints**: Working root name for docs and specs until renamed: `lmlaunch` (placeholder). `lmproxy` is unavailable (taken). Rename trigger: when proxy-server verbs are added.

### [I010] - root command name

- **Prompt**: "D016 picked Option B (named root command). What is the root command name?"
- **User Response**: "Turns out the name lmproxy is already taken so it would cause confusion" — asked whether one CLI should own the launcher and the proxy, and for name suggestions; then chose "Unified CLI" and "Decide later" on the name.
- **Resolution**: D017 recorded: unified CLI ownership confirmed; root name deferred with `lmlaunch` as the working name.
- **Notes**: Name candidates surfaced: `lmp` (recommended; LAMMPS binary overlap footnote), `lmcli`.

### [I011] - harness not installed

- **Prompt**: For D018 – Harness not installed: pick an option, hybridize, or provide your own answer.
- **User Response**: "D018: Option A"
- **Resolution**: D018 resolved as annotate + error: wizard marks unavailable harnesses; direct mode errors with a per-harness install hint before any config action.
- **Notes**: Consistent with the adapter-metadata pattern from D014.

### [I012] - wizard composition

- **Prompt**: For D019 – Wizard composition: pick an option, hybridize, or provide your own answer.
- **User Response**: "D019: Option A"
- **Resolution**: D019 resolved as a setup-capable pipeline: harness → endpoint (default prefilled) → key if needed → model (skippable where the adapter allows).
- **Notes**: First-run vs configured and reachable vs unreachable endpoint states are all in scope for the wizard.

### [D018] - Harness not installed

- **Driver**: Failures should surface before anything touches the user's setup, echoing the D011 principle.
- **Resolved Answer**: Option A — annotate + error.
- **Normalized Requirement**: The wizard shall mark unavailable harnesses as unavailable, and direct mode shall fail with a per-harness install hint before any configuration action runs.
- **Constraints**: Adapters must keep detection and install-hint metadata accurate. Auth-state probing is out of scope for v1 — auth errors surface from the harness itself.

### [D019] - Wizard composition

- **Driver**: One wizard should cover first-run setup and every launch, honoring the saved-default and model-picker decisions.
- **Resolved Answer**: Option A — setup-capable pipeline: harness → endpoint (default prefilled) → key if needed → model (skippable where the adapter allows).
- **Normalized Requirement**: The no-args wizard shall walk harness, endpoint, key (only when needed and unsaved), and model steps in that order, with the endpoint prefilled from the default endpoint and model skip gated by D014 adapter declarations.
- **Constraints**: First-run vs configured and reachable vs unreachable endpoint states must all be handled (D013 graceful degradation applies to the model step).

### [D020] - Glossary terms: Harness, Adapter, Model catalog

- **Driver**: Shared vocabulary keeps adapter specs and the future unified CLI precise.
- **Resolved Answer**: Harness (revised by user: may be a CLI or a GUI app), Adapter, and Model catalog accepted and written to GLOSSARY.md.
- **Normalized Requirement**: GLOSSARY.md shall define Harness, Adapter, and Model catalog with the accepted definitions, kept in sync with this ledger.
- **Constraints**: The Harness revision (CLI or GUI app) means adapter detection and launch mechanics must handle GUI harnesses — confirm at the Phase 2 adapter-architecture branch; D015's stdio inheritance applies to CLI harnesses only.

### [I013] - project structure and layout

- **Prompt**: For T001 – Project structure and layout: pick an option, hybridize, or provide your own answer.
- **User Response**: "T001: Option A"
- **Resolution**: T001 resolved as feature folders (Launch/, Config/, Wizard/, Adapters/, Settings/, Backend/).
- **Notes**: Cross-feature helpers need a shared home outside feature folders.

### [I014] - sub-projects and solution shape

- **Prompt**: For T002 – Sub-projects and solution shape: pick an option, hybridize, or provide your own answer.
- **User Response**: "T002: Option A"
- **Resolution**: T002 resolved as Core + CLI + tests: LmProxy.AgentLauncher.Core plus the app project plus a test project.
- **Notes**: Adapters as a separate published package declined for v1; revisit when community harnesses arrive (D007).

### [I015] - packaging and distribution

- **Prompt**: For T003 – Packaging and distribution: pick an option, hybridize, or provide your own answer.
- **User Response**: "T003: Option C"
- **Resolution**: T003 resolved as both channels: NuGet dotnet tool plus per-OS GitHub Releases binaries.
- **Notes**: Working root name (D017) applies to tool id and binary names until the rename.

### [T001] - Project structure and layout

- **Driver**: Features should map to future subcommands so the unified CLI (D017) inherits the layout.
- **Resolved Answer**: Option A — feature folders: `Launch/`, `Config/`, `Wizard/`, `Adapters/`, `Settings/`, `Backend/`.
- **Normalized Requirement**: Projects shall organize code by feature folders, with per-harness adapter code under an `Adapters/` folder (in the Core project per T002).
- **Constraints**: Cross-feature helpers get a shared location outside the feature folders; folder layout applies within both projects.

### [T002] - Sub-projects and solution shape

- **Driver**: The Core split is the seam the unified CLI (D017) reuses and keeps adapters testable headlessly.
- **Resolved Answer**: Option A — Core + CLI + tests: `LmProxy.AgentLauncher.Core` (adapters, settings, backend client), app project (commands, wizard), test project.
- **Normalized Requirement**: The solution shall contain LmProxy.AgentLauncher.Core, the LmProxy.AgentLauncher app project, and a test project.
- **Constraints**: Adapters are not a separately published package in v1 (Option C declined; revisit when community harnesses arrive, D007). Root rename deferred per D017.

### [T003] - Packaging and distribution

- **Driver**: Harness users are often not .NET developers; distribution must not assume the SDK.
- **Resolved Answer**: Option C — both channels: a NuGet dotnet tool package and per-OS release binaries.
- **Normalized Requirement**: Each release shall publish a dotnet tool package and per-OS release binaries.
- **Constraints**: Two artifact kinds per release must be built and verified in CI; tool id and binary names use the working root name until the D017 rename.

### [I016] - wizard TUI library

- **Prompt**: For T004 – Wizard TUI library: pick an option, hybridize, or provide your own answer.
- **User Response**: "T004: Option A"
- **Resolution**: T004 resolved as Spectre.Console for rendering and prompts, with parsing staying System.CommandLine (D003).
- **Notes**: First new dependency beyond the D003 lock.

### [I017] - adapter architecture

- **Prompt**: For T005 – Adapter architecture: pick an option, hybridize, or provide your own answer.
- **User Response**: "T005: Option A"
- **Resolution**: T005 resolved as one adapter interface plus a static metadata record per harness (kind CLI/GUI, model requirement, install hint, config paths).
- **Notes**: Concrete type signatures deferred to the optional type loop; descriptor model (B) remains available if community adapters arrive.

### [I018] - settings store

- **Prompt**: For T006 – Settings store: pick an option, hybridize, or provide your own answer.
- **User Response**: "T006: Option A"
- **Resolution**: T006 resolved as a JSON settings file in the platform config dir, edited via `config` subcommands, with env-var overrides layered on top.
- **Notes**: Uses the locked M.E.Configuration Json + EnvironmentVariables packages (D003).

### [T004] - Wizard TUI library

- **Driver**: A polished interactive launch experience needs mature console widgets without displacing the locked parser.
- **Resolved Answer**: Option A — Spectre.Console for rendering, selection prompts, and progress; parsing remains System.CommandLine (D003).
- **Normalized Requirement**: The wizard and all console output shall use Spectre.Console; command parsing shall remain System.CommandLine.
- **Constraints**: Prompt UX follows Spectre idioms; wizard step logic (D019) stays framework-agnostic plain code.

### [T005] - Adapter architecture

- **Driver**: The per-harness seam must express every committed per-harness behavior in one typed place.
- **Resolved Answer**: Option A — single adapter interface (detect, configure, build launch) with a static metadata record per harness: kind (CLI/GUI), model requirement, install hint, config paths.
- **Normalized Requirement**: Each harness shall be represented by one adapter implementing detection, configuration, and launch construction, plus a metadata record carrying kind, model requirement, install hint, and config paths.
- **Constraints**: Metadata growth may require interface changes; JSON-descriptor adapters (Option B) remain a later option for community harnesses (D007).

### [T006] - Settings store

- **Driver**: Saved settings must live where the OS and .NET conventions expect, with per-launch escape hatches.
- **Resolved Answer**: Option A — JSON settings file in the platform config dir, edited via `config` subcommands, with env-var overrides layered on top.
- **Normalized Requirement**: Settings shall persist as JSON in the platform config directory; `config` subcommands shall show/set the default endpoint and flavor and set/clear the saved key; environment variables shall override file values.
- **Constraints**: Per-OS paths must be tested and documented; the layering uses the locked M.E.Configuration packages (D003).

### [I019] - model catalog client

- **Prompt**: For T007 – Model catalog client: pick an option, hybridize, or provide your own answer.
- **User Response**: "T007: Option A"
- **Resolution**: T007 resolved as hand-rolled per-flavor HttpClient clients normalizing into the Model catalog.
- **Notes**: Keeps Core dependency-light for T003's packaging.

### [I020] - restore-on-exit mechanics

- **Prompt**: For T008 – Restore-on-exit and interrupt mechanics: pick an option, hybridize, or provide your own answer.
- **User Response**: "T008: Option B"
- **Resolution**: T008 resolved as try/finally + signal handlers plus leftover-backup self-heal on the next launch.
- **Notes**: Self-heal covers hard-kill cases where in-process handlers never run.

### [I021] - testing approach

- **Prompt**: For T009 – Testing approach: pick an option, hybridize, or provide your own answer.
- **User Response**: "T009: Option C"
- **Resolution**: T009 resolved as TUnit with the fakes strategy (temp dirs, stub HTTP handlers, scripted fake harnesses).
- **Notes**: Departs from the session recommendation (xUnit v3) by user choice.

### [T007] - Model catalog client

- **Driver**: A lean, self-contained CLI (T003 tool packaging) favors BCL-only HTTP.
- **Resolved Answer**: Option A — hand-rolled per-flavor HttpClient clients normalizing responses into the Model catalog.
- **Normalized Requirement**: Core shall fetch each flavor's models listing via hand-written HttpClient calls (OpenAI `/v1/models`, shared by both OpenAI flavors; Anthropic `/v1/models`) and normalize responses into the Model catalog.
- **Constraints**: JSON shapes are hand-maintained as the APIs evolve; listing calls must degrade gracefully when the endpoint is unreachable (D013).

### [T008] - Restore-on-exit and interrupt mechanics

- **Driver**: No lasting damage even when the OS kills the process tree (D011 driver).
- **Resolved Answer**: Option B — try/finally plus Ctrl+C/TERM handlers, plus a leftover-backup self-heal that restores residue at the start of the next launch.
- **Normalized Requirement**: Restore-on-exit shall run in try/finally and signal handlers, and every launch shall restore any leftover backup files from a previous abnormal exit before applying new configuration actions.
- **Constraints**: Backup files must carry enough context (path and original bytes) to restore unambiguously; SIGHUP/kill -9 are covered only via self-heal.

### [T009] - Testing approach

- **Driver**: Modern runner features over ecosystem inertia — chosen deliberately over the session recommendation.
- **Resolved Answer**: Option C — TUnit with the fakes strategy: temp-dir config writes, stub HTTP handlers, scripted fake harnesses.
- **Normalized Requirement**: The test project shall use TUnit with fake-based tests covering config writes, settings, backend clients, and lifecycle (D011 restore, D015 foreground).
- **Constraints**: Real-harness integration matrix is a later add-on; this record deliberately supersedes the session's xUnit v3 recommendation (user choice, recorded for traceability).

### [I022] - command and flag naming

- **Prompt**: For T010 – Command and flag naming: pick an option, hybridize, or provide your own answer.
- **User Response**: "Where did --flavor come from?" then "Option A but --flavor is the wrong word for the flag. I want a different word for the endpoint type", then chose `--protocol`.
- **Resolution**: T010 resolved as explicit, probe-free overrides with the endpoint-type flag named `--protocol`; glossary wording revised in D021.
- **Notes**: The clarification was not treated as a selection; T010 was re-asked with the flavor question made explicit before resolving.

### [I023] - saved-backend model

- **Prompt**: For T011 – Saved-backend model: pick an option, hybridize, or provide your own answer.
- **User Response**: "T011: Option C"
- **Resolution**: T011 resolved as a single default backend today with a profile-ready JSON schema (backends list + default pointer).
- **Notes**: Profile verbs stay out of scope for v1; schema tolerates hand-added entries.

### [I024] - exit codes and error contract

- **Prompt**: For T012 – Exit codes and error contract: pick an option, hybridize, or provide your own answer.
- **User Response**: "T012: Option A"
- **Resolution**: T012 resolved as per-class exit codes (0 ok; 2 usage; 3 unknown agent; 4 harness unavailable; 5 endpoint unreachable; 6 required model missing; 7 config/restore failure).
- **Notes**: The code table is a stable, documented contract.

### [T011] - Saved-backend model

- **Driver**: Avoid settings migrations later while keeping today's configuration minimal.
- **Resolved Answer**: Option C — a single default backend today; the JSON schema stores saved backends as a list with a default pointer.
- **Normalized Requirement**: The settings schema shall store saved backends as a list with a default pointer; v1 requires exactly one entry and `config` verbs edit the default entry.
- **Constraints**: Profile verbs (add/switch/list) are out of scope for v1; the schema must tolerate multiple entries without behavior change (v1 reads only the default pointer).

### [T012] - Exit codes and error contract

- **Driver**: Deterministic, scriptable failure classes serve pre-configured launch automation.
- **Resolved Answer**: Option A — per-class exit codes: 0 ok; 2 usage; 3 unknown agent; 4 harness unavailable; 5 endpoint unreachable; 6 required model missing; 7 config/restore failure.
- **Normalized Requirement**: The CLI shall exit with the documented per-class code for each failure class and 0 on success.
- **Constraints**: The code table is a stable contract documented across T003's release channels.

### [T010] - Command and flag naming

- **Driver**: Overrides should be explicit and probe-free so failure classes stay predictable (T012).
- **Resolved Answer**: Option A with `--protocol` naming — raw-URL override requires `--endpoint` plus `--protocol <openai-chat|openai-responses|anthropic>`; `--model <id>` sets a model; `--persist-config` opts into keeping config writes; the saved default needs no flags.
- **Normalized Requirement**: Direct `launch` shall accept `--endpoint`, `--protocol`, `--model`, and `--persist-config` long flags; raw-URL overrides shall require both endpoint and protocol; no protocol probing on the launch path.
- **Constraints**: No short aliases in v1; `--backend <name>` resolution arrives with profile verbs (T011); glossary wording revised in D021.

### [T013] - Wizard endpoint and key persistence

- **Driver**: The wizard must never write a preference; defaults belong to the non-interactive path.
- **Resolved Answer**: Option B, refined — wizard endpoint/key entries apply to the current launch only; the wizard never writes settings; defaults are saved exclusively via non-interactive `config` commands.
- **Normalized Requirement**: Wizard-collected endpoint and key values shall apply to the launched process only; settings changes shall occur only through `config` subcommands.
- **Constraints**: Amends the D019 "setup-capable" framing — the wizard bootstraps a working launch without persisting; first-run users persist via `config` afterwards. The D019 "unsaved key" wizard step asks but never stores.

### [D021] - Glossary revision: protocol wording

- **Driver**: The user rejected "flavor" as the endpoint-type word and chose `--protocol`.
- **Resolved Answer**: The endpoint-type concept is called the "protocol"; the flag is `--protocol` with values `openai-chat`, `openai-responses`, `anthropic`.
- **Normalized Requirement**: GLOSSARY.md's Backend entry shall read "identified by a base URL and a protocol: OpenAI Chat Completions, OpenAI Responses, or Anthropic-compatible", matching this record.
- **Constraints**: Supersedes: D008 (term wording only; D008's three-protocol decision stands unchanged).

### [I025] - wizard endpoint and key persistence

- **Prompt**: For T013 – Wizard endpoint and key persistence: pick an option, hybridize, or provide your own answer.
- **User Response**: "T013: Option B. Wizard shouldn't save a preference for now. The non interactive approach is the one that should allow saving defaults."
- **Resolution**: T013 resolved as apply-once: the wizard never writes settings; defaults are saved exclusively via non-interactive `config` subcommands.
- **Notes**: Amends the D019 "setup-capable" framing — the wizard bootstraps a working launch without persisting.

### [I026] - model pick persistence

- **Prompt**: For T014 – Model pick persistence: pick an option, hybridize, or provide your own answer.
- **User Response**: Clarifying question — "Is this for the non wizard use case?" — then "Option B"
- **Resolution**: T014 resolved as a per-backend default model set only via `config`; the wizard prefills it and `--model` overrides; no launch path writes settings.
- **Notes**: The clarification was mirrored and T014 re-framed before resolving; both launch paths are launch-only by T013's construction.

### [T014] - Model pick persistence

- **Driver**: Non-interactive paths own persistence; launch paths stay write-free (T013 principle).
- **Resolved Answer**: Option B — a per-backend default model set only via `config`; the wizard prefills it and `--model` overrides.
- **Normalized Requirement**: A per-backend default model shall exist in settings, settable only through `config` subcommands; the wizard model step prefills from it; `--model` overrides per launch.
- **Constraints**: The wizard picker still shows when no default model is set; no launch path (wizard or flags) ever writes settings (T013).

### [I027] - persist semantics and self-heal interaction

- **Prompt**: For T015 – Persist semantics and self-heal interaction: pick an option, hybridize, or provide your own answer.
- **User Response**: Redesigned to `--persist <config|model>`; corrected that the wizard-only write prohibition was the intent ("Not using the wizard via the parameterised launch command is not interactively using the program - it can't be by its nature"); confirmed the T008 interaction bug is real ("fair point") and asked which self-heal approach is recommended.
- **Resolution**: T015 resolved as the parameterized `--persist` redesign; T016 records the settings-writer scope correction; self-heal approach recommended (backup markers) and pending user confirmation.
- **Notes**: `--persist model` writes the per-backend default model from the direct launch path.

### [T015] - Persist flag redesign and semantics

- **Driver**: Persistence should be expressible per value type from the non-interactive launch path; the wizard stays write-free.
- **Resolved Answer**: Replace `--persist-config` with parameterized `--persist <config|model>`: `config` keeps harness config-file writes after exit; `model` saves the chosen model as the per-backend default model in settings.
- **Normalized Requirement**: Direct `launch` shall accept `--persist` with values `config` and `model`; `config` suppresses restore-on-exit for that launch's file writes; `model` writes the per-backend default model to settings.
- **Constraints**: Supersedes: T010 (the `--persist-config` flag name; config semantics unchanged). The wizard never writes settings (T013 core stands). Self-heal must not undo deliberately persisted writes — backup files carry a persisted marker and are skipped by self-heal (confirmed; settings-tracked rejected). `--persist` is repeatable with comma-list sugar (confirmed).

### [T016] - Settings-writer scope correction

- **Driver**: Non-interactive use (parameterized launch) is not interactive use; the wizard-only prohibition was the actual principle.
- **Resolved Answer**: Settings may be written by non-interactive paths only — `config` subcommands and explicit `--persist model` on direct launches; the wizard never writes settings.
- **Normalized Requirement**: Settings writes shall originate only from `config` subcommands or explicit `--persist` flags on non-interactive launches; the wizard shall never write settings.
- **Constraints**: Supersedes: T013 and T014 (the "only through config subcommands" scope was broader than intended). D019 remains unchanged (wizard step pipeline unaffected).

### [I028] - persist confirmations

- **Prompt**: "Confirm two things: (1) Self-heal carve-out via backup markers; (2) Combined form: repeatable flag + comma-list sugar."
- **User Response**: "Yes for backup markers. Yes for repeatable and yes for comma list."
- **Resolution**: T015 finalized: backup-marker carve-out adopted; `--persist` repeatable with comma-list sugar.
- **Notes**: Settings-tracked persistence rejected for atomicity, locality, and settings-independence reasons.

### [I029] - ProtocolKind type

- **Prompt**: For T017 – ProtocolKind type: confirm the signature, or revise it.
- **User Response**: "T017: Option A"
- **Resolution**: T017 resolved as a plain enum with OpenAiChat, OpenAiResponses, and Anthropic members.
- **Notes**: Per-protocol HTTP behavior stays in the T007 client switch.

### [I030] - Backend type

- **Prompt**: For T018 – Backend type: confirm the signature, or revise it.
- **User Response**: "T018: Option A"
- **Resolution**: T018 resolved as a flat Backend record (Name, BaseUri, Protocol, optional DefaultModel, optional ApiKey).
- **Notes**: ApiKey plaintext in v1 carries the D012 Phase-2 hardening caveat.

### [I031] - HarnessMetadata type

- **Prompt**: For T019 – HarnessMetadata type: confirm the signature, or revise it.
- **User Response**: "T019: Option A"
- **Resolution**: T019 resolved as a single HarnessMetadata record plus a HarnessKind enum (Cli, Gui).
- **Notes**: Null DetectionCommand means probe the launch command on PATH.

### [T017] - ProtocolKind type

- **Driver**: The protocol needs the leanest closed representation mapping to the `--protocol` flag values.
- **Resolved Answer**: Option A — a plain enum with OpenAiChat, OpenAiResponses, and Anthropic members.
- **Normalized Requirement**: The protocol shall be represented as a ProtocolKind enum with OpenAiChat, OpenAiResponses, and Anthropic members mapping to `--protocol` values openai-chat, openai-responses, and anthropic.
- **Constraints**: Per-protocol HTTP behavior belongs to the T007 client switch, not the value.

### [T018] - Backend type

- **Driver**: Saved backend entries must mirror the T011 settings schema exactly.
- **Resolved Answer**: Option A — a flat Backend record: Name, BaseUri, Protocol, optional DefaultModel, optional ApiKey.
- **Normalized Requirement**: Each saved backend shall be a flat Backend record with name, base URI, protocol, optional default model, and optional API key.
- **Constraints**: ApiKey is plaintext JSON in v1; keychain/DPAPI hardening is a Phase 2 concern (D012).

### [T019] - HarnessMetadata type

- **Driver**: Each adapter must publish D014/D018/D020 facts in one immutable place.
- **Resolved Answer**: Option A — a single HarnessMetadata record (Name, Kind, RequiresModel, InstallHint, optional DetectionCommand, ConfigFileCandidates) plus a HarnessKind enum (Cli, Gui).
- **Normalized Requirement**: Each adapter shall publish a HarnessMetadata record carrying name, kind, model requirement, install hint, optional detection command, and config-file candidates.
- **Constraints**: Null DetectionCommand means probe the launch command on PATH; ConfigFileCandidates are adapter-resolved (project-local or global).

### [I032] - IHarnessAdapter type

- **Prompt**: For T020 – IHarnessAdapter type: confirm the signature, or revise it.
- **User Response**: "T020: Option A"
- **Resolution**: T020 resolved as adapters-plan/Core-executes: adapters return a LaunchPlan; Core executes writes, restores backups, and starts the process.
- **Notes**: LaunchPlan, LaunchRequest, and ConfigurationAction types follow in Round 12.

### [I033] - LauncherSettings type

- **Prompt**: For T021 – LauncherSettings type: confirm the signature, or revise it.
- **User Response**: "T021: Option A"
- **Resolution**: T021 resolved as list + name pointer + schema version.
- **Notes**: Profile verbs later change behavior, not the file format.

### [I034] - ModelCatalog type

- **Prompt**: For T022 – ModelCatalog type: confirm the signature, or revise it.
- **User Response**: "T022: Option A"
- **Resolution**: T022 resolved as a minimal normalized pair: ModelCatalogEntry(Id, DisplayName?) plus fetch time.
- **Notes**: Keeps Core protocol-free; raw API shapes stay in the T007 client.

### [T020] - IHarnessAdapter type

- **Driver**: Core must own backup/restore/self-heal uniformly so the D011 guarantee holds across adapters.
- **Resolved Answer**: Option A — adapters plan, Core executes: the interface exposes Metadata, IsAvailableAsync, and PlanLaunchAsync returning a LaunchPlan.
- **Normalized Requirement**: Each adapter shall implement Metadata, asynchronous availability detection, and asynchronous launch planning returning a LaunchPlan; Core shall execute writes, restore backups, and start the process.
- **Constraints**: Adapters are pure planners; harness quirks that cannot fit the plan types re-open the ConfigurationAction family (T023).

### [T021] - LauncherSettings type

- **Driver**: Profile verbs later should change behavior, not the file format.
- **Resolved Answer**: Option A — list + name pointer + schema version: LauncherSettings(Backends: List<Backend>, DefaultBackend: string, SchemaVersion: int).
- **Normalized Requirement**: Settings shall store a backends list, a default backend pointer by name, and an integer schema version.
- **Constraints**: None.

### [T022] - ModelCatalog type

- **Driver**: The wizard picker needs a protocol-free normalized listing from both APIs.
- **Resolved Answer**: Option A — minimal normalized pair: ModelCatalogEntry(Id, DisplayName?) and ModelCatalog(Models, FetchedAt).
- **Normalized Requirement**: The T007 client shall normalize both APIs into ModelCatalog entries carrying an Id and an optional display label, plus the fetch time.
- **Constraints**: Fields beyond Id/label are re-fetched if ever needed; raw API shapes stay in the client.

### [I035] - ConfigurationAction family

- **Prompt**: For T023 – ConfigurationAction family: confirm the family, or revise it.
- **User Response**: "T023: Agree with proposal"
- **Resolution**: T023 resolved as the proposed three-variant family with no variant expansion.
- **Notes**: Harness quirks that cannot fit the three variants re-open the family.

### [T023] - ConfigurationAction family

- **Driver**: Adapters must express every D010 channel in one closed family that Core executes uniformly (T020).
- **Resolved Answer**: Family confirmed as proposed: abstract ConfigurationAction with CliArgumentAction(Argument, Value?), ConfigFileWriteAction(TargetPath, Content), and EnvironmentVariableAction(Name, Value); no variant expansion requested.
- **Normalized Requirement**: Configuration actions shall form a closed family of exactly CliArgumentAction, ConfigFileWriteAction, and EnvironmentVariableAction under one abstract base record.
- **Constraints**: Harness quirks that cannot fit these three variants re-open the family.

### [I036] - LaunchPlan and LaunchRequest types

- **Prompt**: For T024 – LaunchPlan and LaunchRequest types: confirm the signatures, or revise them.
- **User Response**: "Option A but I have a question on the choice of ProcessStartInfo - Does it make sense to use CliInvoke's ProcessConfiguration primitive instead of ProcessStartInfo so that less back and forth conversion is needed?"
- **Resolution**: T024 resolved as Option A with ProcessConfiguration replacing ProcessStartInfo in LaunchPlan (swap confirmed).
- **Notes**: User-suggested swap; beta-API churn re-opens T024 if shapes move.

### [T024] - LaunchPlan and LaunchRequest types

- **Driver**: Core's runner needs one self-contained handoff from adapter to execution (T008/D015).
- **Resolved Answer**: Option A with a swap (user suggestion) — LaunchPlan.Process carries CliInvoke's ProcessConfiguration instead of System.Diagnostics.ProcessStartInfo, avoiding conversion back and forth.
- **Normalized Requirement**: Adapters shall accept a LaunchRequest(Backend, Model?, WorkingDirectory) and return a LaunchPlan(CliInvoke ProcessConfiguration, ConfigurationActions), which Core executes directly.
- **Constraints**: Launch types are coupled to CliInvoke 3.0.0-beta.3's API shape; beta churn re-opens this record.

<!-- next-d: D022 -->
<!-- next-t: T025 -->
<!-- next-i: I037 -->
