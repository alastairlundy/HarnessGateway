# Decision Ledger — HarnessGateway: per-model protocols

### [D001] - session goal

- **Driver**: one provider identity should serve models with differing wire protocols under a single shared API key, without duplicated backends.
- **Resolved Answer**: "Single provider identity, per-model protocols, end to end."
- **Normalized Requirement**: The session shall produce a design where one provider entry holds one endpoint and one API-key reference, and each model within it may declare its own protocol, resolved at launch end to end.
- **Constraints**: Track is concept-then-implementation; Phase 2 runs explicitly. Current multi-backend workaround and the future proxy direction are context, not commitments. Coverage-sweep exclusions: deployment/hosting, observability, concurrency, performance, and changes to the no-secrets-in-JSON invariant are out of scope.

### [D002] - provider versus Backend

- **Driver**: the endpoint's true identity is the provider serving models, not a protocol variant of it; users think in providers, and the glossary should match that.
- **Resolved Answer**: "Option A — Provider is a new entry type."
- **Normalized Requirement**: A Provider entry shall own one endpoint, one API-key reference, and a model list where each model names its own protocol; launch resolves the protocol per model from that list.
- **Constraints**: CLI verbs, config keys, adapter read paths, and glossary change as one coordinated surface change. The fate of the existing `Backend` concept was deliberately left open in this pick; extracted as follow-up branch D003.

### [D003] - Provider glossary term

- **Driver**: users think in providers; the glossary must match the entry type the configuration now centers on.
- **Resolved Answer**: "Provider — An API identity that owns one endpoint, one API key reference, and a set of models, where each model may use its own wire protocol."
- **Normalized Requirement**: GLOSSARY.md shall define Provider as: An API identity that owns one endpoint, one API key reference, and a set of models, where each model may use its own wire protocol.
- **Constraints**: The Backend and Model catalog glossary definitions stay unrevised until D004 resolves.

### [D004] - fate of the Backend surface

- **Driver**: one entry type deserves one name everywhere; a dual vocabulary is documentation debt paid forever.
- **Resolved Answer**: "Option A — One breaking rename."
- **Normalized Requirement**: The CLI surface and settings keys shall use provider naming (`provider` verbs, `--provider`, `defaultProvider`, `providers.<name>.*`); saved `backends` entries shall migrate to providers on read with a schemaVersion bump.
- **Constraints**: `backend` invocations and docs break at this version; the README config section is rewritten; the exit-code contract stays append-only; deprecated `backend` aliases are explicitly declined.

### [D005] - key sharing across providers

- **Driver**: a key belongs to exactly one provider identity; co-mingling one stored secret across providers breaks isolation of rotation and revocation.
- **Resolved Answer**: "Option C — One key per provider, no sharing."
- **Normalized Requirement**: Each provider shall own exactly one private key reference; two providers shall never share a stored secret reference, even when the underlying upstream key string is identical.
- **Constraints**: Models within one provider share that provider's single key by construction (per D002). `provider set-key` stays per-provider; no shared key namespace is introduced. Re-entering the same secret for a second provider is accepted.

### [D006] - model-to-protocol binding

- **Driver**: single-protocol providers should not pay a per-model declaration tax; exceptions should be explicit.
- **Resolved Answer**: "Option B — Provider default plus per-model overrides."
- **Normalized Requirement**: Each provider shall carry one default protocol; a model entry may declare its own protocol, which wins at launch; an undeclared model resolves to the provider default.
- **Constraints**: Two sources of protocol truth with defined precedence, model entry winning. An undeclared model silently inherits the provider default. Whether `launch` accepts or refuses an undeclared `--model` name is an open point carried to Phase 2. Migration maps an old backend's protocol to the provider default and its `defaultModel` to the undeclared-model case.

### [T001] - Provider domain location

- **Driver**: the tree should carry the domain's own names; a stale folder name misleads every reader after the surface rename.
- **Resolved Answer**: "Option A — Rename to Core/Providers."
- **Normalized Requirement**: The folder and namespace shall become Core/Providers; the Provider and model domain types shall live there; Backend type names shall be replaced in the same move.
- **Constraints**: The rename PR touches most of Core, adapters, and tests at once; no parallel Backends namespace survives; legacy LmProxy-style artifact names are not resurrected.
- **Cites**: D002, D004, D006

### [T002] - settings schema shape

- **Driver**: a predictable, ordered model list keeps the wizard and scripts honest; JSON maps hide order.
- **Resolved Answer**: "Option A — Models array on Provider."
- **Normalized Requirement**: A provider entry shall serialize with a models array where each entry carries a model name and an optional protocol override; the provider entry shall keep one default protocol and one optional defaultModel.
- **Constraints**: Overriding a model's protocol requires listing that model; default-protocol models are implicit, preserving the D006 precedence. Model entry names must be unique within a provider.
- **Cites**: D006, T001

### [T003] - migration mechanics

- **Driver**: a stale settings file should converge to the current schema automatically; users should not have to run a migration command.
- **Resolved Answer**: "Option B — Rewrite at load."
- **Normalized Requirement**: Every settings load that detects the old backends schema shall convert it to providers and rewrite the file through the BackupService flow.
- **Constraints**: Conversion runs after SelfHealAsync backup replay; conversion is idempotent; a failed rewrite leaves the old file intact with the error surfaced.
- **Cites**: D004

### [T004] - provider CLI surface

- **Driver**: verb-level validation keeps the closed key set small and puts errors next to the mistake.
- **Resolved Answer**: "Option B — Model sub-verbs."
- **Normalized Requirement**: Model entries shall be managed through `provider model add|remove|list`; typed config keys shall stay provider-level (`defaultProvider`, `providers.<name>.endpoint|.protocol|.defaultModel`) beside read-only `schemaVersion`/`providers`.
- **Constraints**: Models are not scriptable through `config set`; both surfaces (keys and verbs) are documented; provider-remove key cleanup is extracted as T005.
- **Cites**: D004, D005

### [T005] - provider remove key cleanup

- **Driver**: symmetric ownership; a removed identity should leave no secret behind.
- **Resolved Answer**: "Option A — Remove deletes the key."
- **Normalized Requirement**: `provider remove` shall delete the provider entry and the key stored under its reference in the credential store.
- **Constraints**: Key-deletion failure is reported and does not block entry removal; re-adding the same provider name requires re-entering the key; `provider clear-key` remains the manual path.
- **Cites**: D005, T004

### [T006] - launch resolution and the undeclared model

- **Driver**: a misroute caught before launch beats a warning ignored at launch; the resolution path is already strict by design.
- **Resolved Answer**: "Option A — Refuse unlisted model."
- **Normalized Requirement**: When the selected provider has a models array and `--model X` names no entry, launch shall fail as a user-rectifiable refusal with a best-effort close-match hint; when the provider has no models array, any model name launches on the provider default protocol.
- **Constraints**: The refusal reuses exit code 3 (no new exit code; the README table keeps its size); the hint text stays non-contractual; precedence stays model entry, then provider default (D006).
- **Cites**: D006, T002

### [T007] - adapter emission under per-model protocol

- **Driver**: one mapping tested once beats six copies drifting apart.
- **Resolved Answer**: "Option B — Shared Core planner."
- **Normalized Requirement**: A shared Core component shall map the resolved (provider, model, protocol) to the protocol's env-var set and base config shape once; all six adapters shall consume its output when building LaunchPlan.
- **Constraints**: The planner feeds LaunchPlan.Process.EnvironmentVariables only; per-harness config shapes (for example OpenCode's inline session config) stay adapter-owned; unrelated protocols' env vars are never emitted.
- **Cites**: D006, T001

### [T009] - documentation scope

- **Driver**: broken script authors need a pointer to the new spelling; docs should not grow beyond what the rename touched.
- **Resolved Answer**: "Option A plus wizard help text update."
- **Normalized Requirement**: The README config section shall be rewritten for the provider surface, a migration note shall be added, and wizard help text shall be updated; the exit-code table keeps its size per T006.
- **Constraints**: `docs/agents/out-of-scope.md` recheck happens at implementation time, not as part of this docs pass; no new exit-code rows.
- **Cites**: D004, T006, T004

### [T008] - wizard steps under providers

- **Driver**: the catalog should show every model the endpoint serves, regardless of which protocol listing hides it.
- **Resolved Answer**: "Option B — Per-protocol fetch and merge."
- **Normalized Requirement**: The wizard shall fetch the catalog once per protocol kind and merge results into one listing, tagging each entry with the protocols it was found under.
- **Constraints**: Duplicate names across protocols dedupe to one entry offering all found protocols, preselecting the provider default when present; per-protocol requests may be three in the worst case.
- **Cites**: T002, D006

### [T010] - layer boundaries

- **Driver**: adapters should depend on an abstraction they can fake in tests, not on static collateral they must shim.
- **Resolved Answer**: "Option C — Injected service."
- **Normalized Requirement**: The emission planner shall sit behind an interface resolved through DI and handed to adapters; registration happens at the exe's composition root.
- **Constraints**: Core/Providers hosts the interface and its implementation (per T011, T012); the composition root is Program.cs-level wiring; the launch flow gains one resolution step; tests fake the interface instead of shimming statics.
- **Cites**: T007, T011, T012

### [T011] - dependency direction

- **Driver**: one inward arrow into Core/Providers keeps the planner testable without adapters.
- **Resolved Answer**: "Option A — Adapters consume planner types."
- **Normalized Requirement**: Planner output types shall be defined in Core/Providers; adapters shall reference them; the planner and its interface shall never reference Core/Adapters.
- **Constraints**: Harness-specific emission additions stay adapter-side beside the consumed types; no neutral contract namespace is introduced.
- **Cites**: T007, T010, T012

### [T012] - separation mechanism

- **Driver**: concentrate the change in one place; the launcher owns the whole codebase.
- **Resolved Answer**: "Option A — Folders in Launcher.Core."
- **Normalized Requirement**: Provider, resolution, planner, and migration types shall live as folders under Core/Providers within Launcher.Core; the two-project solution layout is unchanged.
- **Constraints**: No new assemblies; `InternalsVisibleTo` stays unchanged; folder boundaries carry the separation alone.
- **Cites**: T001, T007

### [T013] - launch protocol override

- **Driver**: one precedence rule for the whole launch; explicit flags should never lose to stored state.
- **Resolved Answer**: "Option A — Flags win wholesale."
- **Normalized Requirement**: When `--endpoint` and `--protocol` are both present on launch, they shall replace the session's endpoint and protocol wholesale; stored model-entry and provider-default protocols are ignored for that launch.
- **Constraints**: The flag pair stays the only admissible form for launch overrides (only-valid-together unchanged); the T006 unlisted-model guard does not apply to overridden launches; no new exit codes.
- **Cites**: D006, T006

### [T014] - Provider record

- **Driver**: field-for-field migration from today's Backend avoids a conversion surprise; the models list is the only structural addition.
- **Resolved Answer**: "Option A — Flat per T002, with the unlisted-default refinement: a DefaultModel missing from a non-empty models array is refused, not resolved."
- **Normalized Requirement**: Provider shall be a sealed record with Name, BaseUri, DefaultProtocol, optional DefaultModel, an ApiKey reference, and a Models list defaulting to empty; when the models list is non-empty, the effective model (DefaultModel when no --model is given) shall name a listed entry, and a DefaultModel absent from the list is refused like any unlisted model.
- **Constraints**: The default-as-entry-flag shape is declined; T002's provider-level defaultModel stands; ApiKey is an opaque reference at rest, never secret material. The refusal reuses the T006 guard verbatim (exit 3, close-match hint). An empty or absent models list keeps the open-model path with DefaultProtocol.
- **Cites**: T002, D005, T006

### [T015] - ModelEntry record

- **Driver**: the entry carries the smallest override representation; null is the unmarked case.
- **Resolved Answer**: "Option A — Name plus nullable protocol."
- **Normalized Requirement**: ModelEntry shall be a sealed record with a required Name and a nullable Protocol where null means the provider default applies.
- **Constraints**: Entry names are unique within a provider (T002); resolution branches on null Protocol per the D006 precedence.
- **Cites**: T002, D006

### [T016] - ProtocolEmission record

- **Driver**: named properties keep adapter consumption explicit and directly assertable.
- **Resolved Answer**: "Option B — Typed properties."
- **Normalized Requirement**: ProtocolEmission shall expose Protocol, BaseUrlVariable, BaseUrlValue, and an ExtraVariables dictionary; adapters shall map these into LaunchPlan.Process.EnvironmentVariables.
- **Constraints**: Changing per-protocol extras touches this record; the flat-dictionary and pairs-list shapes are declined.
- **Cites**: T007, T011

### [T017] - planner interface

- **Driver**: plain-value inputs keep the mapping pure and always fakeable.
- **Resolved Answer**: "Option A — Two inputs, one output."
- **Normalized Requirement**: IProtocolEmissionPlanner shall expose Plan(ProtocolKind, Uri) returning ProtocolEmission; callers compute the effective protocol before calling.
- **Constraints**: API-key variable naming rides in ExtraVariables as implementation detail; the whole-result and async signatures are declined.
- **Cites**: T010, T016

### [T018] - provider-resolution result

- **Driver**: handing the planner's exact inputs forward prevents recomputation drift.
- **Resolved Answer**: "Option A — Provider plus effective pair."
- **Normalized Requirement**: ProviderResolution shall carry the whole Provider plus the effective Model string and effective ProtocolKind after precedence and override layering.
- **Constraints**: Failure shapes mirror BackendResolutionFailure unchanged; adapters compute no protocol themselves.
- **Cites**: D006, T013, T014

### [T019] - settings shape

- **Driver**: one array and one pointer keep the file readable and the wizard ordered.
- **Resolved Answer**: "Option A — Providers array plus pointer."
- **Normalized Requirement**: LauncherSettings shall carry SchemaVersion, DefaultProvider, and a Providers array; the loader shall read the legacy shape for migration and write only the current shape.
- **Constraints**: schemaVersion bumps once for the migration; the old backends field is not preserved by design (the D004 break); dictionary-keyed providers are declined.
- **Cites**: D004, T003

<!-- next-d: D007 -->
<!-- next-t: T020 -->
