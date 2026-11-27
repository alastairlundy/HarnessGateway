# Harness model quirks — pi's harness-side model resolution

Maintainer-facing detail behind the short known-harness-quirks note in the README's *Models and provider disambiguation* section. The two documents describe the same behavior and are updated together when pi changes. This note documents pi's resolver; it does not change it — pi's resolver itself is out of scope for the launcher.

## How pi resolves a bare model name

Pi matches a bare `--model` value against **its own** models.dev-derived catalog — the catalogs shipped with pi plus the cached refresh in `~/.pi/agent/models-store.json` — never against the launcher's saved Provider entries or their Model catalog. Every such reference lives in a **Harness-side provider** namespace: a provider identity a harness uses in its own model catalog to resolve model references, distinct from a Provider, which is a saved launcher entry.

The resolution rules (`resolveCliModel` in `model-resolver.ts`):

- Matching is case-insensitive over every model pi knows about, by model id or by `provider/model` id; `provider/`-shaped input scopes the search to that provider.
- Exactly one exact match is taken outright, authenticated or not.
- Several exact matches are ambiguous: pi prefers the sole provider with configured authentication, and otherwise refuses with `Model "…" is ambiguous across providers: <list>. … Use --provider or provider/model.`

So a name that is unambiguous against the launcher's Provider can still be ambiguous harness-side, and vice versa — the launcher's qualification at handoff exists because these two namespaces do not share a resolution authority.

## Ambient environment-variable authentication widens the ambiguity

A provider counts as authenticated when pi's runtime snapshot lists it as configured (`hasConfiguredAuth` → `snapshot.configuredProviders` in `model-runtime.ts`), and credentials include the known API-key environment variables inherited from the spawned process environment (`packages/ai/src/env-api-keys.ts`, e.g. `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`; pi's provider table is in `packages/coding-agent/docs/providers.md`).

Because a spawned harness inherits the caller's environment by default, an unrelated credential-bearing environment variable makes additional harness-side providers "usable". That changes which provider pi picks for a bare name and widens the set that counts as ambiguous — the motivation for the **Environment Variable Scrub**, removal of credential-bearing environment variables from the spawned harness process environment at the caller's request, whose launcher-side environment is never scrubbed and which, absent the request, leaves the child inheriting everything.

## Older pi builds misreported ambiguity

Older builds emitted `More than one matching provider is authenticated.` while exactly one matching provider was authenticated. The hint is not a count: the wording is chosen by branch (`usableCount === 0` → "No matching provider is authenticated.", otherwise the plural sentence), so any ambiguous result carrying at least one usable provider shows the plural claim. When triaging user reports, read the string as "the ambiguity could not be settled by authentication", not as a statement about how many providers matched.

## Evidence trail

- `packages/coding-agent/src/core/model-resolver.ts` — `resolveCliModel`: catalog-wide exact matching, the sole-authenticated-provider preference, and the ambiguity error with its authentication hint.
- `packages/coding-agent/src/core/model-runtime.ts` — `hasConfiguredAuth` reading `snapshot.configuredProviders`, the configured/authenticated state the resolver consults.
- `packages/ai/src/env-api-keys.ts` — the provider-to-environment-variable map behind that state (`findEnvKeys` / `getEnvApiKey`).
- Source: <https://github.com/earendil-works/pi>.
