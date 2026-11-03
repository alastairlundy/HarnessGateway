**Title:** Existing LLM API proxies analogous to a future HarnessGateway proxy server
**Date:** 2026-10-30
**Question:** What existing tools provide an analogous LLM API proxy/gateway — i.e. proxy or translate between OpenAI-compatible and Anthropic-compatible protocols, or act as local/self-hosted LLM gateways, ideally with a way to get agent harnesses (Claude Code, OpenCode, Kilo, Pi) pointed at them? The goal is to evaluate existing functionality before deciding whether HarnessGateway should build its own proxy-server mode (one provider entry owning one endpoint + one key + a model list where each model may declare its own wire protocol).

*Note: `docs/research/` is a new directory/convention for this repo — there was no existing research-notes folder. All star counts, licenses and push dates below were pulled from the GitHub REST API / repo pages on 2026-10-30.*

## Fit for HarnessGateway's plan (summary)

- **Closest single match:** [musistudio/claude-code-router (CCR)](https://github.com/musistudio/claude-code-router) — a *local* model-gateway + control plane (default endpoint `http://127.0.0.1:3456`) that speaks OpenAI Chat/Responses, Anthropic Messages and Gemini on the inbound side, and ships per-harness **agent config profiles** that configure Claude Code, Codex, Grok CLI, Kimi CLI, **Kilo Code, OpenCode, Pi**, ZCode and WorkBuddy [README](https://github.com/musistudio/claude-code-router). It is hovering next to ~38k stars and very active. If CCR existed a year ago, HarnessGateway's launcher niche would have overlapped heavily; it is the benchmark to test against before building.
- **Also very close:** LiteLLM — a heavyweight self-hostable AI gateway with an Anthropic-compatible inbound endpoint (`/v1/messages`) alongside the OpenAI-format surface, *plus* a beta Python SDK (`litellm.agent()`) that spins up a local endpoint and points harness runtimes (Claude Code, Codex, **OpenCode**, pi, Deep Agents) at it, keeping provider keys out of the sandbox [README](https://github.com/BerriAI/litellm) / [docs](https://docs.litellm.ai/docs/harness). But it is a Python/Rust server+SDK stack, not a single-binary CLI talking to agent configs.
- **Four-protocol inbound** is already commodity: new-api (Go, AGPLv3) exposes OpenAI Chat/Responses, Anthropic Messages and Gemini endpoints and has a dedicated conversion module ("RelayKit") for translation among its four text protocols [README](https://github.com/QuantumNous/new-api). The wire-translation problem HarnessGateway's per-model-protocol decision targets is solved, repeatedly, in open source.
- **What no surveyed tool does exactly what HarnessGateway plans:** none of them model *one provider identity = one endpoint + one key + a per-model wire protocol* — they model providers with fixed inbound families and (at most) per-channel protocols; none integrate key management with an OS credential store; and none are a small single-binary CLI whose proxy mode is bolted onto an existing launcher/wizard/config-injection flow. CCR's "agent profiles" are the nearest analog of the launcher's config injection, but they live in a desktop-app/browser-UI control plane rather than a one-shot launch command.
- **Caveat on the premise:** for OpenCode specifically, pointing a harness at an Anthropic-compatible/custom endpoint is largely a config problem the harness handles natively (HarnessGateway already does this by env/config injection), and OpenRouter now exposes a first-party *Anthropic-compatible input* ("Anthropic Skin") that removes the need for a local translation proxy for Claude Code [OpenRouter docs](https://openrouter.ai/docs/guides/guides/claude-code-integration). Translation proxies that exist mainly "so Claude Code can use OpenRouter" (e.g. [y-router](https://github.com/luohy15/y-router)) have been archived as superseded — a signal that the pure one-way translation niche is being eaten by gateways and providers.
- **Recommendation signal:** test CCR (harness-injection control plane) and LiteLLM (protocol surface + virtual keys) against the planned proxy verbs; a self-build is only justified if what's wanted is specifically "the launcher gains `serve`/`proxy` verbs, per-model protocols, and OS-key-store-backed keys, as one thin binary."

---

## 1. Local / self-hosted gateways

### LiteLLM (BerriAI/litellm)

- **What it does:** Open-source "AI Gateway" — a Python SDK *and* a proxy server that gives one unified interface to 100+ provider APIs "using the OpenAI format". Deployable as a centralized gateway with virtual keys, spend tracking, guardrails, load balancing, admin dashboard. [README: "Call any LLM in OpenAI format… Use it as a Python SDK … or deploy the AI Gateway (Proxy Server)"](https://github.com/BerriAI/litellm)
- **Protocol surface:** Inbound on the proxy: OpenAI-format endpoints (`/chat/completions`, `/responses`, `/embeddings`, …) **and** an Anthropic-format `/messages` endpoint — README's endpoint/provider matrix lists `/messages` as a supported endpoint and shows Anthropic with "✅" under it ([provider table](https://github.com/BerriAI/litellm). "All Supported Endpoints — `/chat/completions`, `/responses`, `/embeddings`, … `/a2a`, `/messages` and more."). Outbound: provider-specific translated calls per provider (each row of the provider matrix declares which inbound endpoints work against which provider backends). The repo description also advertises a "Rust core with Python SDK" ([repo description, GitHub API, 2026-10-30](https://api.github.com/repos/BerriAI/litellm)) — note the docs site additionally has a "Rust AI Gateway (beta)" nav item ([docs sidebar](https://docs.litellm.ai/docs/harness)).
- **Key mgmt:** per-provider keys configured in gateway config (env/DB) plus **virtual keys** issued by the proxy; an enterprise commercial tier adds SSO/SSO-style features. [README features table](https://github.com/BerriAI/litellm)
- **Harness injection:** yes — recently added, beta. `litellm.agent(Harness.CLAUDE_CODE | CODEX | OPENCODE | ... , ...)`: "For each session, `litellm.agent()` starts a small model endpoint on the host and points the runtime at it with the runtime's own base URL setting, such as `ANTHROPIC_BASE_URL` for Claude Code. The runtime gets a random token that only works for that session… Neither your virtual key nor any provider key enters the sandbox." ([docs.litellm.ai/docs/harness](https://docs.litellm.ai/docs/harness)) This is SDK-driven injection (Python orchestrates the launch), not CLI-driven — but the mechanism (temp local endpoint + env pointing the harness at it + session-scoped token) is essentially the same design as a proxy-backed launcher.
- **License:** repo license field is NOASSERTION via GitHub API; docs footer says "MIT license" and the README routes enterprise features to a "LiteLLM Commercial License" ([docs footer](https://docs.litellm.ai/docs/harness), [Enterprise section](https://github.com/BerriAI/litellm)). Treating it as MIT core + commercial enterprise tier; verified at file level only by the docs-site footer claim.
- **Maintenance:** extremely active — `pushed_at` 2026-10-30 (same day as research), ~62.4k stars ([GitHub API](https://api.github.com/repos/BerriAI/litellm)).
- **How run:** Python (`uv tool install 'litellm[proxy]'`, `litellm --model gpt-4o`), Docker, Terraform modules for AWS/GCP. [README](https://github.com/BerriAI/litellm)
- **2026-10-31 update — the `lite` CLI is a launcher-shaped surface** ([docs/proxy/management_cli](https://docs.litellm.ai/docs/proxy/management_cli)): a thin "laptop install" client to a LiteLLM proxy deployment whose agent commands (`lite claude`, `lite codex`, `lite opencode`) resolve and pre-verify the key (fail fast before the agent starts), export per-agent env vars (`ANTHROPIC_BASE_URL` set to the proxy *root* so `/v1/messages` appends, stray `ANTHROPIC_API_KEY` cleared; `OPENAI_BASE_URL`+`OPENAI_API_KEY`; Codex additionally driven by `-c` overrides because it ignores `OPENAI_BASE_URL`), then replace themselves with the agent process. `lite login` stores the short-lived credential (default 24h) in the **OS keychain** (Keychain/Credential Manager/Secret Service via `keyring`), with only the non-secret half in `~/.litellm/token.json` (`0600` file, `0700` dir). Flag: the launcher binds to a LiteLLM *gateway deployment* — `LITELLM_PROXY_URL`, LiteLLM SSO/PKCE/JWT credentials, virtual keys, teams/budgets/guardrails — not to arbitrary user-chosen endpoints; it supports Claude Code, Codex and OpenCode only (no Kilo/Pi). This narrows the "single-binary CLI + OS keychain exists only as the launcher" packaging-gap claim from the earlier observations — note the launcher also defaults to a file credential if `keyring`/`litellm[cli]` extra is missing.

### LiteLLM-based Anthropic→OpenAI bridge: 1rgs/claude-code-proxy

- **What it does:** "A proxy server that lets you use Anthropic clients (like Claude Code) with Gemini, OpenAI, or Anthropic models themselves… all via LiteLLM." Receives Anthropic-format requests, translates to OpenAI format through LiteLLM, converts the response back. [README, "How It Works"](https://github.com/1rgs/claude-code-proxy)
- **Protocol surface:** Anthropic Messages in → OpenAI (or Gemini/Anthropic) out; streaming + non-streaming. [README](https://github.com/1rgs/claude-code-proxy)
- **Key mgmt:** plain `.env` file (OPENAI_API_KEY / GEMINI_API_KEY / optional ANTHROPIC_API_KEY); no key store, no virtual keys. [README setup section](https://github.com/1rgs/claude-code-proxy)
- **Harness injection:** documented Claude Code recipe only: `ANTHROPIC_BASE_URL=http://localhost:8082 claude`; maps Claude model classes (`haiku`/`sonnet`) to `SMALL_MODEL`/`BIG_MODEL`. [README "Using with Claude Code"](https://github.com/1rgs/claude-code-proxy)
- **License:** **none found** — GitHub API returns `license: null` for the repo (checked 2026-10-30). Flag: no LICENSE file, so reuse/contribution terms are unclear.
- **Maintenance:** moderately active — pushed 2026-06-23, ~3.8k stars, 70 open issues ([GitHub API](https://api.github.com/repos/1rgs/claude-code-proxy)).
- **How run:** FastAPI/uvicorn (`uv run uvicorn server:app --port 8082`) or Docker (`ghcr.io/1rgs/claude-code-proxy:latest`). [README](https://github.com/1rgs/claude-code-proxy)

### y-router (luohy15/y-router) — archived

- **What it does:** "A Cloudflare Worker that translates between Anthropic's Claude API and OpenAI-compatible APIs, enabling you to use Claude Code with OpenRouter and other OpenAI-compatible providers." [README](https://github.com/luohy15/y-router)
- **Protocol surface:** Anthropic `/v1/messages` in → OpenAI chat-completions out, responses translated back; "Supports both streaming and non-streaming responses." [README "What it does"](https://github.com/luohy15/y-router)
- **Key mgmt:** none at the router — the OpenRouter API key is passed through from the client (`x-api-key` header); no key store. [README API usage](https://github.com/luohy15/y-router)
- **Harness injection:** only the standard Claude Code env pattern (`ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL`, …) documented as shell aliases. [README](https://github.com/luohy15/y-router)
- **License:** MIT ([GitHub API](https://api.github.com/repos/luohy15/y-router) and README license section).
- **Maintenance:** **archived** — README banner: "This project is archived. OpenRouter now provides an official integration for Claude Code." Last push 2026-01-11, 383 stars ([GitHub repo metadata](https://api.github.com/repos/luohy15/y-router)). Useful as evidence that the "OpenAI→Anthropic skin so Claude Code can use other providers" niche has been commoditized.
- **How run:** Cloudflare Worker (wrangler deploy), Docker Compose, or the author's hosted instance. [README](https://github.com/luohy15/y-router)

### Claude Code Router (musistudio/claude-code-router, "CCR") — closest overall match

- **What it does:** "a local model gateway and control plane for coding agents… gives Claude Code, Claude Design, Codex, Grok CLI, Kimi CLI, Kilo Code, OpenCode, Pi, ZCode, WorkBuddy, and compatible API clients **one stable local endpoint**, while you manage the providers, models, accounts, routing rules, and tools behind it from one place." Local gateway listens on `http://127.0.0.1:3456` by default. [README "Why use Claude Code Router?"](https://github.com/musistudio/claude-code-router)
- **Protocol surface:** "CCR supports OpenAI Chat / Responses, Anthropic Messages, Gemini Generate Content / Interactions, OpenRouter, DeepSeek, SiliconFlow, Moonshot, Kimi Code, Mistral, Z.AI, Bailian, and custom compatible providers." [README](https://github.com/musistudio/claude-code-router) So multi-protocol inbound *and* multi-provider outbound, plus "protocol probing" and "model discovery" on custom endpoints [Core capabilities table](https://github.com/musistudio/claude-code-router).
- **Key mgmt:** single keys and **credential pools** per provider, with key rotation; separate CCR client keys with expiration and rate/token limits. [Core capabilities table](https://github.com/musistudio/claude-code-router)
- **Harness injection:** this is its core feature — "Agent Config: choose Claude Code, Claude Design, Codex, Grok CLI, Kimi CLI, Kilo Code, OpenCode, Pi, ZCode, or WorkBuddy, select a model, and apply the profile"; the profiles cover "model overrides; scopes; environment settings; CLI and app launch entries" [README Quick Start / Core capabilities](https://github.com/musistudio/claude-code-router). This is exactly the launcher's job (point a named harness at endpoint+key+model) done from a GUI/CLI instead of a one-shot command. It does the config-file editing for you (the README frames it as "Switch providers or models without changing your workflow or repeatedly editing agent configuration files").
- **License:** MIT (README license badge + section; [LICENSE](https://github.com/musistudio/claude-code-router/blob/main/LICENSE)).
- **Maintenance:** active — pushed 2026-10-10, ~37.9k stars, v3 series with a desktop app (v3.1.3 downloads referenced in README). [GitHub API](https://api.github.com/repos/musistudio/claude-code-router)
- **How run:** desktop app (macOS/Windows/Linux downloads), npm CLI (`npm i -g @musistudio/claude-code-router`, Node 22+, `ccr ui`), Browser UI on `:3458` + gateway on `:3456`, or Docker. [README Quick Start](https://github.com/musistudio/claude-code-router)
- **Fit note:** CCR bundles a lot more (plugins, Chrome login import, AgentClaw chat relays, sponsor-heavy commercial ecosystem). The *gateway + profiles* slice is the part analogous to HarnessGateway.

### new-api (QuantumNous/new-api)

- **What it does:** "A self-hosted AI gateway for applications, agents, and teams. Connect upstream model services, expose a consistent API to your clients, and manage routing, access, usage, and costs in one place." Fork/successor of one-api. [README](https://github.com/QuantumNous/new-api)
- **Protocol surface:** inbound OpenAI Chat/Responses (`/v1/chat/completions`, `/v1/responses`), **Anthropic Messages (`/v1/messages`)**, Gemini (`/v1beta/models/{model}:generateContent|:streamGenerateContent`), plus WebSocket realtime/Responses, images/audio, embeddings/rerank. Conversion between "the four text protocols" is encapsulated in [RelayKit](https://github.com/QuantumNous/new-api/blob/main/relaykit/README.md) — "provides request, response, and streaming conversion between the four text protocols," with the caveat that "protocol-specific tools and fields may not map exactly." [README protocols table](https://github.com/QuantumNous/new-api)
- **Key mgmt:** upstream keys per *channel* (multiple upstream keys per channel); New API issues its own API keys with group/model access, quotas, rate limits; user management with OAuth/OIDC, passkeys, 2FA. [README capabilities](https://github.com/QuantumNous/new-api)
- **Harness injection:** none documented — clients are pointed at `/v1` with an API key ([Quick start step 4](https://github.com/QuantumNous/new-api)); no per-harness config-writing feature visible in the README.
- **License:** AGPLv3, with Section 7 additional terms (attribution + UI link to the original project required in modified versions). [README License section](https://github.com/QuantumNous/new-api)
- **Maintenance:** very active — pushed 2026-10-30, ~51k stars. [GitHub API search result](https://api.github.com/search/repositories?q=new-api+org:QuantumNous)
- **How run:** Go server (Docker/Compose) + web console (React 19); SQLite/MySQL/Postgres + optional Redis/ClickHouse. This is a server-with-database, not a lightweight CLI. [README deployment](https://github.com/QuantumNous/new-api)
- **Fit note:** best evidence that "gateway that serves OpenAI-, Anthropic- and Gemini-protocol clients from shared upstream channels" is a solved problem at scale — but its shape (web console + DB + teams/quotas) is much heavier than a local launcher adjunct, and AGPLv3 may matter if code were borrowed.

### one-api (songquanpeng/one-api)

- **What it does:** "LLM API management & key redistribution system, unifying multiple providers under a single API. Single binary, Docker-ready" ([repo description, GitHub search API](https://api.github.com/search/repositories?q=one-api+user:songquanpeng)). Inbound and usage are OpenAI-format: "使用方式与 OpenAI API 一致" (usage is identical to the OpenAI API) — requests to non-OpenAI-format channels are "中继并修改请求体和返回体" (relayed with request/response body rewriting). [README](https://github.com/songquanpeng/one-api)
- **Protocol surface:** OpenAI-format in only (no Anthropic-format inbound documented); outbound channels include Anthropic Claude (and AWS Claude), Gemini, Mistral, many Chinese providers, Ollama, etc. [README 功能 feature list](https://github.com/songquanpeng/one-api)
- **Key mgmt:** the point of the tool — upstream API keys go in *channels*, users mint *access tokens* with expiry/quota/model/IP restrictions; load balancing across channels. [README](https://github.com/songquanpeng/one-api)
- **Harness injection:** none; client setup is generic `OPENAI_API_BASE`/API-key pointing. [README 使用方法](https://github.com/songquanpeng/one-api)
- **License:** MIT **with an added condition**: "必须在页面底部保留署名以及指向本项目的链接" (must keep attribution + link in the page footer, also for forks). [README 注意 section](https://github.com/songquanpeng/one-api)
- **Maintenance:** slowed — last push 2026-01-09 (~10 months before this research), ~37.3k stars; the ecosystem has largely moved to new-api. [GitHub search API](https://api.github.com/search/repositories?q=one-api+user:songquanpeng) (status claim inferred from the push date, flagged as an inference.)
- **How run:** single Go executable, Docker, `--port --log-dir` flags; SQLite/MySQL/Postgres. Its "single binary" shape is the closest packaging analog to HarnessGateway's per-OS binaries. [README 部署](https://github.com/songquanpeng/one-api)

### Claude Relay Service (Wei-Shaw/claude-relay-service)

- **What it does:** self-hosted **account-pool relay** for Anthropic (Claude OAuth subscriptions), OpenAI/Codex, Gemini and "Droid" — "多账户管理" (multi-account management) with per-user API keys, routing/rotation on failures, usage stats, admin console. Aimed at splitting Claude Max subscriptions ("拼车"/car-pooling) [README](https://github.com/Wei-Shaw/claude-relay-service). Note the README itself warns use "可能违反Anthropic的服务条款" (may violate Anthropic's ToS). [README disclaimer](https://github.com/Wei-Shaw/claude-relay-service)
- **Protocol surface:** multi-protocol inbound by route prefix: `/api/` (Anthropic/Claude Code), `/openai/` (Codex, Responses `wire_api`), `/gemini/`, `/droid/claude`, `/droid/openai`, `/droid/comm/v1` (OpenAI-compatible generic); upstream is account pools, not just API keys (Claude accounts added via OAuth authorization flow). [README usage & third-party endpoint sections](https://github.com/Wei-Shaw/claude-relay-service)
- **Key mgmt:** admin-generated per-user keys (`cr_…`) with rate/concurrency/model/client restrictions (client allowlisting by User-Agent, e.g. claude-cli, Gemini CLI); accounts stored server-side. [README](https://github.com/Wei-Shaw/claude-relay-service)
- **Harness injection:** documents config/env changes for Claude Code (`ANTHROPIC_BASE_URL`/`ANTHROPIC_AUTH_TOKEN`), Codex (`~/.codex/config.toml` model_provider block), Gemini CLI, Droid (`~/.factory/config.json`), VSCode Claude plugin — done manually by the user, not automated by the tool. [README sections 4](https://github.com/Wei-Shaw/claude-relay-service)
- **License:** MIT (+ security notice: earlier versions (≤v1.1.248) had an admin auth-bypass; project recommends upgrading or migrating to "CRS 2.0 (sub2api)"). [README top notices](https://github.com/Wei-Shaw/claude-relay-service)
- **Maintenance:** active — pushed 2026-10-29, ~12.8k stars. [GitHub search API](https://api.github.com/search/repositories?q=claude-relay-service+user:Wei-Shaw)
- **How run:** Node 18+ service + Redis, Docker Compose, install script. [README deployment](https://github.com/Wei-Shaw/claude-relay-service)
- **Fit note:** closest to a "multi-user gateway other people depend on"; different motivation (subscription pooling) than HarnessGateway's endpoint/protocol focus.

## 2. SaaS / hosted gateways (comparison points)

### OpenRouter

- **What it does:** hosted gateway/failover layer with **first-party Anthropic-compatible input**: "OpenRouter exposes an input that is compatible with the Anthropic Messages API… OpenRouter's 'Anthropic Skin' behaves exactly like the Anthropic API." Claude Code connects with no local proxy: `ANTHROPIC_BASE_URL=https://openrouter.ai/api`, OpenRouter key as `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY=""`. Also warn-model-anthropic-first: only guaranteed with the first-party Anthropic provider. [Collections of the above: Claude Code integration guide](https://openrouter.ai/docs/guides/guides/claude-code-integration)
- **Key mgmt / harness injection:** OpenRouter-issued keys; harness "injection" is the standard env-var recipe the user applies (with explicit ordering and `/logout` repro notes). Same source.
- **License/run:** hosted SaaS (no OSS core surveyed here; docs only).
- **Fit note:** demonstrates the env-var contract Claude Code expects from a gateway (`ANTHROPIC_BASE_URL` + `ANTHROPIC_AUTH_TOKEN` vs `ANTHROPIC_API_KEY` semantics) — the exact contract a HarnessGateway proxy would need to satisfy when serving Anthropic-protocol harnesses; cite the reasoning paragraph above as the normative description.

### Cloudflare AI Gateway

- **What it does:** managed proxy in front of provider APIs: analytics, logging, caching, rate limiting, request retries and model fallback; "By connecting your apps to AI Gateway…" per-provider gateway URLs (e.g. openai, anthropic paths). [Overview page](https://developers.cloudflare.com/ai-gateway/)
- **Protocol surface:** provider-shaped (pass-through per provider path) rather than a universal multi-protocol inbound; OpenAI/Anthropic/Gemini/etc. are gatewayed individually (providers list: Workers AI, Anthropic, Google Gemini, OpenAI, Replicate…). No harness config injection. Same source.
- **License/run:** closed SaaS (Cloudflare platform), usable without self-hosting; not directly comparable to a local CLI adjunct.
- **Fit note:** shows what a *managed* gateway adds that a small local one never would: global network, logs, cache, analytics.

### Portkey AI Gateway

- **What it does:** open-source, TypeScript, single-process gateway, "Route to 1,600+ LLMs, 50+ AI Guardrails with 1 fast & friendly API"; runs locally via `npx @portkey-ai/gateway` on `http://localhost:8787/v1`; config-driven retries/fallbacks/load-balancing/guardrails; virtual keys and RBAC are in the hosted/enterprise offering. [README](https://github.com/Portkey-AI/gateway) Gateway 2.0 note: core enterprise gateway "merging into open-source with our 2.0 release" pre-release branch. Same source.
- **Protocol surface:** OpenAI-format inbound ("use any OpenAI-compatible client or API", SDK examples are OpenAI-style); outbound 45+ providers incl. Anthropic. README providers table shows Anthropic ✅ streaming. No Anthropic-format inbound documented in the README (flag: would need docs site check beyond what was verified here). [README](https://github.com/Portkey-AI/gateway)
- **License:** MIT (GitHub API: license MIT). ~13.3k stars; last push 2026-05-25 on the main gateway repo (sibling repos like Portkey-AI/models active into 2026-10, so treat "last push" per-repo, not per-org). [GitHub search API](https://api.github.com/search/repositories?q=gateway+org:Portkey-AI)
- **Fit note:** the *lightweight* OSS gateway extreme (122 kb footprint claim); good for testing thin-proxy performance expectations, weak on the Anthropic-wire side and on harness integration.

### Helicone

- **What it does:** "AI Gateway & LLM Observability Platform": gateway gives 100+ models under one key "through the OpenAI API" (`https://ai-gateway.helicone.ai`); primary value is logs/traces/cost/sessions/prompts. Self-hostable (Docker Compose; five components incl. Cloudflare Worker + ClickHouse). [README](https://github.com/Helicone/helicone)
- **Protocol surface:** OpenAI-format inbound only per README (Anthropic is a supported *inference provider* behind the gateway, and Anthropic SDK integration is logged, but no native `/v1/messages` inbound is claimed in this README — flagged as unverified beyond the README). [README](https://github.com/Helicone/helicone)
- **License:** Apache-2.0 (GitHub API + README license section). ~6.2k stars, pushed 2026-10-28. [GitHub search API](https://api.github.com/search/repositories?q=helicone+user:Helicone)
- **Fit note:** observability-first; the gateway is an access layer, and self-hosting refunds the infra complexity (5 services), not the launch-adjacency HarnessGateway cares about.

## 3. Meta

- **"Meta's LLMProxy" was not findable as an open-source project.** Searches of the GitHub API across `facebookresearch`, `facebook`, and `facebookincubator` orgs for `llm-proxy` / `llm`+`gateway` returned no repositories (queries executed 2026-10-30: `q=llm proxy org:facebookresearch` → 0 results; `q=llm gateway org:facebook org:facebookincubator org:facebookresearch` → 0 results). If the "Meta LLMProxy" in circulation refers to Meta's internal LLM gateway system, it does not appear to have been open-sourced under that name; flagged as unverifiable from primary sources.
- **Meta's open-source gateway-adjacent artifact is Llama Stack, renamed OGX (`ogx-ai/ogx`):** fetching the old `meta-llama/llama-stack` README redirects to an announcement: "**Llama Stack is now OGX.** The name changed, and so did the mission — model-agnostic, multi-SDK, production-grade." OGX is "a drop-in replacement for the OpenAI API that you can run anywhere" with `/v1/chat/completions`, `/v1/embeddings`, Responses API, and notably native multi-protocol inbound: "use the Anthropic SDK (`/v1/messages`) or Google GenAI SDK (`/v1alpha/interactions`) natively alongside the OpenAI API." ([README](https://raw.githubusercontent.com/meta-llama/llama-stack/main/README.md), license MIT per [LICENSE](https://raw.githubusercontent.com/ogx-ai/ogx/main/LICENSE)). That makes it the only surveyed tool whose *inbound* surface natively spans OpenAI + Anthropic + Google GenAI SDKs — relevant prior art for per-model protocol negotiation, though it is a serving platform (pluggable inference providers), not a harness-injection launcher. Maintenance status not pulled (star/push data not captured) — flagged.

## 4. Comparison table

| Tool | Protocol in / out | Self-host | Harness injection | License | Status (2026-10-30) |
|---|---|---|---|---|---|
| LiteLLM ([repo](https://github.com/BerriAI/litellm)) | In: OpenAI-format endpoints + Anthropic `/messages`; Out: 100+ providers translated | Yes (server; Docker/Terraform) | Yes — beta SDK `litellm.agent()` drives Claude Code/Codex/OpenCode/pi via temp local endpoint + env (`ANTHROPIC_BASE_URL`) | MIT core + commercial enterprise tier (README/docs) | Very active |
| claude-code-proxy ([1rgs](https://github.com/1rgs/claude-code-proxy)) | In: Anthropic Messages → Out: OpenAI/Gemini/Anthropic (via LiteLLM) | Yes (FastAPI/Docker) | Claude Code env recipe only (`ANTHROPIC_BASE_URL`) | **none found** | Moderate |
| y-router ([luohy15](https://github.com/luohy15/y-router)) | In: Anthropic Messages → Out: OpenAI-compat (OpenRouter default) | Yes (CF Worker/Docker) | Claude Code env recipe only | MIT | **Archived** (superseded by OpenRouter's official integration) |
| Claude Code Router ([musistudio](https://github.com/musistudio/claude-code-router)) | In: OpenAI Chat/Responses + Anthropic Messages + Gemini; Out: 12+ named providers + custom | Yes (local gateway `:3456`, desktop app/npm CLI/Docker) | **Yes — agent config profiles write each harness's config/env** (Claude Code, Codex, Kilo Code, OpenCode, Pi, Grok CLI, Kimi CLI, ZCode, WorkBuddy) | MIT | Active (v3) |
| new-api ([QuantumNous](https://github.com/QuantumNous/new-api)) | In: OpenAI Chat/Responses + Anthropic Messages + Gemini (+WS); conversion in RelayKit; Out: many channels | Yes (Go server + DB, Docker) | No | **AGPLv3** (+§7 terms) | Very active |
| one-api ([songquanpeng](https://github.com/songquanpeng/one-api)) | In: OpenAI-format only; Out: Anthropic/Gemini/etc. via body-rewriting channels | Yes (single Go binary + DB) | No | MIT + attribution condition | Slowed (pushed 2026-01) |
| claude-relay-service ([Wei-Shaw](https://github.com/Wei-Shaw/claude-relay-service)) | In: Anthropic (`/api/`), OpenAI Responses (`/openai/`), Gemini, Droid variants; Out: OAuth account pools | Yes (Node+Redis/Docker) | Manual config docs for Claude Code/Codex/Gemini CLI/Droid | MIT | Active; ToS-risk purpose; recent auth-bypass notice |
| OpenRouter (SaaS) | In: OpenAI + **Anthropic-compatible "Anthropic Skin"**; Out: many upstreams incl. failover | No (hosted) | Env-var recipe for Claude Code gateway connection | Proprietary (SaaS) | Hosted product |
| Cloudflare AI Gateway (SaaS) | In/Out: per-provider passthrough paths (no universal Anthropic-in found on survey) | No (hosted) | No | Proprietary (SaaS) | Managed product |
| Portkey ([repo](https://github.com/Portkey-AI/gateway)) | In: OpenAI-format; Out: 45+ providers | Yes (npx/Docker/CF Workers) + hosted | No (framework-agent integrations only) | MIT | Active (2.0 pre-release) |
| Helicone ([repo](https://github.com/Helicone/helicone)) | In: OpenAI-format gateway; Out: 100+ providers | Yes (Docker; 5 components) | No | Apache-2.0 | Active |
| OGX / Llama Stack ([ogx-ai/ogx](https://raw.githubusercontent.com/meta-llama/llama-stack/main/README.md)) | In: OpenAI (`/v1/chat/completions`, `/v1/responses`) **+ Anthropic SDK `/v1/messages` + Google GenAI SDK**; Out: pluggable providers (Ollama/vLLM/managed) | Yes (`ogx go` / uv / pip) | No | MIT | Active (renamed from meta-llama/llama-stack); stars/push not pulled |

## Observations for the build-vs-test decision

1. **The wire-translation layer is commodity; the harness-injection layer is the differentiator.** Every serious gateway translates OpenAI↔Anthropic (LiteLLM, new-api, CCR, y-router, claude-code-proxy). Only CCR (config profiles) and LiteLLM's beta agent SDK automate getting a harness pointed at the gateway — and CCR's profiles cover exactly the harnesses HarnessGateway supports (Claude Code, OpenCode, Kilo, Pi) plus more. [CCR README](https://github.com/musistudio/claude-code-router), [LiteLLM harness docs](https://docs.litellm.ai/docs/harness)
2. **The env-var contract is documented from the gateway side.** OpenRouter's guide is a precise, first-party description of what Claude Code expects from a bearer-token gateway (`ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN` vs `ANTHROPIC_API_KEY`, cached-login pitfalls) — a free test oracle for proxy-mode integration. [OpenRouter docs](https://openrouter.ai/docs/guides/guides/claude-code-integration)
3. **No tool matches the per-model-protocol data model.** The surveyed data models are provider-oriented with protocol-per-inbound-family (LiteLLM, new-api RelayKit) or channel+model-list (new-api, one-api). The per-model wire protocol ("each model may declare its own protocol", that design's model) did not appear in any surveyed README — flagged as not found, not proven absent.
4. **Packaging gap:** single-binary local CLI + OS key chain exists only as the launcher (HarnessGateway); gateways ship as Python servers, npm/Electron apps, Go+DB consoles, or SaaS. A one-binary `harnessgateway serve` binding the existing orchestrator/config/backup/key-store machinery to an HTTP proxy would occupy unoccupied shape, though LiteLLM's sandbox/session-token design and CCR's profiles design both merit testing first.

*Every unflagged claim above is drawn from the linked primary source (repo README, official docs page, or GitHub API metadata) fetched on 2026-10-30. Inferred/uncertain items are marked "flagged".*

## Alternatives to CCR (added 2026-10-31)

**Question:** what *other* tools could fill CCR's role (local gateway serving Anthropic/OpenAI-protocol inbound **plus** per-harness setup automation), for testing against the launcher? Scope: tools not already surveyed above; all repo metadata below pulled from the GitHub API / linked READMEs on **2026-10-31**. Lead-name resolutions: the "copilot-api proxy" lead is **ericc-ch/copilot-api** (not guangzaco); the "aigw" CLI lead is **Envoy AI Gateway**, which has been renamed **Agent Router**; the "bedrock-access-gateway" lead resolved but is **archived/deprecated**. No GitHub project named "coco" or "yolo" surfaced in the searches run for this section (unverified — nothing found, not proven absent); awesome-*/ list sweeps were not performed this pass.

### Picks for testing

- **opencodex** ([lidge-jun/opencodex](https://github.com/lidge-jun/opencodex)) — the closest full CCR substitute: a local triple-protocol gateway + an `ocx` CLI that natively configures Codex/Claude Code/Claude Desktop/Grok Build and writes managed config files for ~25 more clients (incl. OpenCode, Pi, Kilo Code, Kimi CLI), with `ocx stop` restoring the original config.
- **CC Switch** ([farion1231/cc-switch](https://github.com/farion1231/cc-switch)) — the config-profile layer done at the largest scale in this space (~150k stars); includes a built-in *Local Routing* proxy that converts between all four text protocols with per-tool failover.
- **copilot-api** ([ericc-ch/copilot-api](https://github.com/ericc-ch/copilot-api)) — smallest concrete test of the "subscription-backed local API + generated harness launch command" pattern (`start --claude-code`); Copilot is its only upstream.
- (runner-up) **ccNexus** ([lich0821/ccNexus](https://github.com/lich0821/ccNexus)) — lightweight Go GUI/headless endpoint-rotator for Claude Code + Codex if a single-binary-plus-manual-config shape is what's being compared.

## Gateway + harness automation (direct CCR substitutes)

### opencodex (lidge-jun/opencodex)

- **What it does:** "One local gateway for your AI coding tools and models… accept[s] the Responses, Chat Completions and Anthropic Messages APIs and translate[s] each request into whatever your provider speaks" — providers (40+ built-ins: Claude, Gemini, Grok, GLM, DeepSeek, Kimi, Qwen, Ollama, Qoder, OpenRouter, any OpenAI-compatible endpoint) are configured from a web dashboard on `http://localhost:10100`. Also manages a **ChatGPT account pool** for Codex auth (quota-aware routing, thread affinity, failover) and virtual "combos" (failover/weighted round-robin model ids). [README](https://github.com/lidge-jun/opencodex)
- **Protocol surface:** inbound `POST /v1/responses`, `POST /v1/chat/completions`, `POST /v1/messages` ([client support page](https://opencodex.me/reference/client-support/)); outbound per-provider translation incl. native OAuth logins (xAI, Anthropic, Kimi; ChatGPT via `codex login` forwarding). Web search/vision "sidecars" give non-OpenAI models those features.
- **Harness injection:** the strongest found anywhere — a generated, test-locked **client support matrix**: *native* integration for Codex, Claude Code, Claude Desktop, Grok Build ("Set up by opencodex itself; no exported config file" — `ocx claude` launches Claude Code through the proxy; `ocx init` wires Codex and offers a `codex` shim); *managed-file integration* for OpenCode (JSON), Pi (JSON), Kilo Code (JSON), Kimi CLI (TOML), WorkBuddy (JSON), ZCode (JSON), Factory Droid, T3 Code, Cline, Oh My Pi, Hermes, OpenClaw (JSON5), Qoder, Raycast and ~10 more. `ocx stop` "restore[s] Codex to its original configuration"; `ocx uninstall` removes all state. Same source.
- **Key mgmt:** provider keys/OAuth tokens stored in `~/.opencodex/config.json` + `ocx-state`; `${ENV_VAR}` references supported; binding beyond loopback **requires** a bearer token (`OPENCODEX_API_AUTH_TOKEN`, checked per request). [README](https://github.com/lidge-jun/opencodex)
- **License:** MIT (GitHub API license + README badge).
- **Maintenance:** extremely active — `pushed_at` 2026-10-31 (day of research), ~18.1k stars created 2026-06 (fastest-growing project surveyed). Flag: **single maintainer** — the code-signing policy names lidge-jun as sole committer and approver ([README](https://github.com/lidge-jun/opencodex)).
- **How run:** npm CLI (`npm i -g @bitkyc08/opencodex`, Node 18+, Bun runtime bundled), background service (`ocx service` per-OS), desktop app beta (dmg/msi/AppImage/deb), Docker Compose (remote hub), source. Community-managed, explicitly not affiliated with any provider.

### CC Switch (farion1231/cc-switch)

- **What it does:** Tauri-2 desktop "All-in-One assistant" for **12 tools** — Claude Code, Claude Desktop, Codex, Gemini CLI, Grok Build, OpenCode, OpenClaw, Hermes, Pi, MiniMax Code, Kimi Code, Oh My Pi — "Switch API providers in one click… no more hand-editing JSON / TOML / YAML config files", with 90+ provider presets, plus MCP/Skills/Prompts management, session reading, and tray switching. [README](https://github.com/farion1231/cc-switch)
- **Protocol surface:** three modes per tool — **Direct** (tool points straight at provider via rewritten config), **Local Routing** (built-in local proxy that "converts requests between Anthropic Messages, OpenAI Chat Completions, OpenAI Responses, and Gemini Native", with per-tool failover queues, circuit breaker, health monitoring, and a "Rectifier" that fixes upstream-incompatible request features), **Aggregation** (one model list spanning several providers; design credited to opencodex). [README](https://github.com/farion1231/cc-switch)
- **Harness injection:** writes each tool's own config and only replaces "the connection details — endpoint, key, and model"; "CC Switch automatically backs up each config file before rewriting it"; auto-imports existing config as a `default` provider on first launch; two modes per tool family — *Switch* (one active provider) and *Coexist* (multiple providers written into OpenCode/Pi/Kimi Code/etc. own configs for in-tool picking); on quit it writes the direct configuration back before routing ends. This is the launcher's exact job, done by GUI.
- **Key mgmt:** keys sit inside the per-provider profiles it writes; not OS-key-store-backed. Subscription logins (GitHub Copilot, ChatGPT, xAI, Kimi) manageable in "Accounts" (beta, ToS-risk noted in README).
- **License:** MIT (GitHub API + README badge).
- **Maintenance:** extremely active — `pushed_at` 2026-10-31, ~150.6k stars, ~9.9k forks, signed/notarized macOS builds, Homebrew/paru packages, commercial-relay sponsor ecosystem (README shows ~30 paid relay sponsors).
- **How run:** native desktop app only (Windows msi/portable, macOS dmg/brew, Linux deb/rpm/AppImage); no CLI/headless mode visible.
- **Fit note:** the highest-adoption tool overlapping HarnessGateway's launcher niche; its Local Routing makes it also a gateway, so it tests both halves (profile-writing config surgery and proxy behavior) against the launcher at once.

### copilot-api (ericc-ch/copilot-api)

- **What it does:** "A reverse-engineered proxy for the GitHub Copilot API that exposes it as an OpenAI and Anthropic compatible service… including to power Claude Code." Subscription-backed local API (the same class as claude-relay-service, but single-user). [README](https://github.com/ericc-ch/copilot-api)
- **Protocol surface:** inbound OpenAI (`/v1/chat/completions`, `/v1/models`, `/v1/embeddings`) **and** Anthropic (`/v1/messages`, `/v1/messages/count_tokens`); upstream is GitHub Copilot only. [README endpoints tables](https://github.com/ericc-ch/copilot-api)
- **Harness injection:** yes, partially — `copilot-api start --claude-code` prompts for primary/small models and **copies a launch command to the clipboard** that sets the Claude Code env vars (`ANTHROPIC_BASE_URL=http://localhost:4141`, `ANTHROPIC_AUTH_TOKEN=dummy`, `ANTHROPIC_MODEL`, `ANTHROPIC_SMALL_FAST_MODEL`, …); a `.claude/settings.json` recipe is documented for permanent setup. Claude Code only. Same source.
- **Key mgmt:** GitHub OAuth device flow / provided token, stored under the platform's local-share path; `auth` subcommand for CI.
- **License:** MIT (GitHub API).
- **Maintenance:** moderate — last push 2025-11-10 (~1 year before this research), ~4.1k stars, supported. README warns of GitHub abuse-detection/ToS risk. [GitHub API](https://api.github.com/repos/ericc-ch/copilot-api)
- **How run:** `npx copilot-api@latest start` (Bun/TypeScript), Docker (port 4141), source. Web usage dashboard included.
- **Fit note:** note it documents: "If you are using opencode, you do not need this project. Opencode supports GitHub Copilot provider out of the box" — consistent with the repo's launch-path assumption that harnesses increasingly absorb provider integrations natively.

### ccNexus (lich0821/ccNexus)

- **What it does:** "Intelligent API gateway for Claude Code and Codex CLI — rotate endpoints, monitor usage" — multi-endpoint failover, format conversion "Claude、OpenAI、Gemini 格式互转", Codex **token pool** (batch-imported `access_token/refresh_token` with auto-rotation/refresh/invalidation), per-endpoint model override, `/v1/models`, usage stats, WebDAV sync. [README (zh)](https://github.com/lich0821/ccNexus), [English docs](https://github.com/lich0821/ccNexus/blob/master/docs/README_EN.md)
- **Protocol surface:** client-side converter choice per endpoint: Claude / OpenAI Chat / OpenAI Responses / Gemini; port 3000, optional Basic Auth, headless/Docker mode. Same source.
- **Harness injection:** none automated — README documents manual `~/.claude/settings.json` (`ANTHROPIC_BASE_URL` + `ANTHROPIC_AUTH_TOKEN`) and `~/.codex/config.toml` `[model_providers.ccNexus]` blocks (its own GitHub topics include `claude-code-router`).
- **License:** MIT (badge + [LICENSE](https://github.com/lich0821/ccNexus/blob/master/LICENSE)).
- **Maintenance:** active — pushed 2026-10-30, ~1.0k stars. [GitHub API](https://api.github.com/repos/lich0821/ccNexus)
- **How run:** Wails desktop GUI (Win/macOS/Linux releases), pure-backend headless HTTP mode, Docker. Go binary per platform — closest *packaging* analog to HarnessGateway's per-OS binaries among the substitutes with a GUI.

### anyclaude (coder/anyclaude)

- **What it does:** "Claude Code with any LLM" — an npm CLI wrapper: it "spawns a simple HTTP server that translates between Anthropic's format and the AI SDK format, enabling support for any AI SDK provider", then launches Claude Code pointed at it (`ANTHROPIC_BASE_URL`). [README](https://github.com/coder/anyclaude)
- **Protocol surface:** Anthropic in → OpenAI/Google/xAI out via Vercel AI SDK; model syntax `openai/*`, `google/*`, `xai/*`; `OPENAI_API_URL` for OpenRouter-style endpoints; GPT-5 reasoning-effort/service-tier passthrough flags. Same source.
- **Harness injection:** the wrapper itself sets the env and launches `claude` — one-command launch, Claude Code only; `/model openai/*` switching inside the UI works.
- **License:** MIT (GitHub API). ~290 stars, last push 2025-08-12 — **stale** (flag: predates CCR's v3 era).
- **How run:** npm package (`pnpm i -g anyclaude`), published by the Coder org.

## Gateway-quality tools without harness automation (manual-setup flags)

### Agent Router (theagentrouter/agent-router, formerly **Envoy AI Gateway**)

- **What it does:** "The open source control plane for AI and agent traffic, powered by Envoy… one consistent, OpenAI-compatible API for every model and tool" (Agentic AI Foundation project; repo moved from `envoyproxy/ai-gateway`, CLI and APIs unchanged). Resolves the "aigw" CLI lead: **the CLI is still `aigw`**. [README](https://github.com/theagentrouter/agent-router)
- **Protocol surface:** standalone local mode: `OPENAI_API_KEY=sk-… aigw run` → OpenAI-compatible router on `http://localhost:1975/v1` ("without any dependencies such as docker or Kubernetes", CLI explicitly *experimental*); production shape is Envoy/K8s. Outbound providers include Anthropic, Bedrock, Vertex, Gemini, Groq, DeepSeek, Grok, Mistral, etc. No Anthropic-format inbound found in the README (flag: they advertise "provider auto-configuration" in the CLI guide, but the detail page fetched was page-thin — unverified). [README + CLI guide](https://theagentrouter.ai/docs/cli/)
- **Key mgmt:** env-var keys for standalone; `BackendSecurityPolicy`/CRD-based on K8s.
- **Harness injection:** none.
- **License:** Apache-2.0 (GitHub API). ~2.3k stars, pushed 2026-10-29 — active with same maintainers/cadence as Envoy AI Gateway.
- **How run:** Go binaries/Docker/Helm (K8s) or the `aigw` CLI standalone.
- **Fit note:** the *enterprise-grade* pole of the gateway side; heavier shape than CCR and no harness wiring, but natively multi-protocol translation engineering humming along on Envoy.

### gpt-load (tbphp/gpt-load)

- **What it does:** self-hosted AI gateway "for multi-channel, multi-credential setups — API keys and subscription accounts, scheduling, failover, request logs and usage", web-admin-managed. [README/repo](https://github.com/tbphp/gpt-load)
- **Protocol surface:** OpenAI/Anthropic/Gemini-format groups upstream-side (repo topics include `anthropic`, `claude-code`, `codex`, `gemini`); key pools with auto-detection of valid formats. No harness config automation.
- **Key mgmt:** the point of the tool — per-channel multi-credential pools, weights, retries, health checks.
- **License:** MIT. Very active (pushed 2026-10-29), ~7.2k stars, Go + web dashboard, Docker / pre-built binaries + DB. [GitHub API](https://api.github.com/repos/tbphp/gpt-load)
- **Fit note:** a manual-setup middle ground between one-api/heavyweight gateways and CCR — single process, web UI, but no harness profiles.

### claude-code-proxy (fuergaosi233/claude-code-proxy) — distinct from 1rgs's entry above

- **What it does:** reverse direction to 1rgs's tool: serves a Claude-compatible `/v1/messages` so **Claude Code can run on OpenAI-compatible providers** (OpenAI, Azure, Ollama, any `OPENAI_BASE_URL`), with BIG/MIDDLE/SMALL env-var model mapping (haiku/sonnet/opus), tool-call conversion, streaming, custom-header injection. [README](https://github.com/fuergaosi233/claude-code-proxy)
- **Harness injection / key mgmt:** none — `.env` config and the standard `ANTHROPIC_BASE_URL=http://localhost:8082 claude` recipe.
- **License:** MIT; Python/FastAPI; Docker; pushed 2026-03-12, ~2.8k stars (slowing; flag). [GitHub API](https://api.github.com/repos/fuergaosi233/claude-code-proxy)

### bedrock-access-gateway (aws-samples) — archived, *and its premise is now solved upstream*

- **Status:** README banner: "**This project is deprecated.** Amazon Bedrock now serves OpenAI-compatible and Anthropic-compatible APIs natively… Call Amazon Bedrock directly" (Claude models via the native [Anthropic Messages API](https://docs.aws.amazon.com/bedrock/latest/userguide/inference-messages-api.html); GPT-class models via OpenAI-compatible "bedrock-mantle" Chat/Responses APIs). Archived on GitHub. [README](https://github.com/aws-samples/bedrock-access-gateway)
- **Why it still matters here:** second concrete case (after y-router) of a translation gateway made unnecessary by the *provider* adding protocol-nativity — relevant risk weighting for building a proxy-mode into the launcher. (~1.0k stars, Python/FastAPI, MIT-0; OpenAI-format inbound, Secrets-Manager-issued gateway key; API Gateway+Lambda or Fargate deployment — cloud-shaped, not local.)

## Subscription/account-pool relays (same class as claude-relay-service, cross-referenced for completeness)

### sub2api (Wei-Shaw/sub2api) — the CRS 2.0 successor

- **What it does:** "AI API Gateway Platform for Subscription Quota Distribution" — upstream accounts (OAuth or API-key: Claude, OpenAI/Codex, Gemini, xAI Grok, TypeSafe) pooled server-side; users get platform-issued `sk-` keys; token-level billing, concurrency/rate limits, sticky sessions, admin dashboard, built-in payments. Positioned as the successor this repo's earlier survey noted ("CRS 2.0 (sub2api)"). [README](https://github.com/Wei-Shaw/sub2api)
- **Protocol surface:** broad inbound: `/v1/messages` (Anthropic; also converts xAI Responses into Anthropic Messages output "for Claude CLI style clients"), `/v1/responses` incl. Codex-style Responses **WebSocket ingress** (with `http_bridge` fallback), `/v1/chat/completions`, image/video media endpoints. [README Grok/OpenAI sections](https://github.com/Wei-Shaw/sub2api)
- **Harness injection:** partial and UI-mediated — a "Use Key" modal on the API-key page generates the correct per-client config "file and base URL" for **Grok CLI** (writing `~/.grok/config.toml` contents) and exposes an **OpenCode** tab; Claude Code/Codex get manual recipes elsewhere (e.g. the Nginx sticky-session note). Not launcher-style automated writes.
- **Key mgmt:** platform keys per user/group; upstream OAuth handled with PKCE flows (xAI client details overridable).
- **License:** **LGPL-3.0**; Go+Vue+Postgres+Redis; one-click systemd install or Docker Compose; ~45.2k stars, pushed 2026-10-31 — README carries prominent ToS/non-commercial notices.

### Also in this class (brief)

- **antigravity-claude-proxy** ([badrisnarayanan](https://github.com/badrisnarayanan/antigravity-claude-proxy)), MIT, ~4.0k stars, pushed 2026-10-23 — exposes Google **Antigravity**'s Claude/Gemini models for Claude Code and OpenClaw.
- **WindsurfAPI** ([dwgx/WindsurfAPI](https://github.com/dwgx/WindsurfAPI)), MIT, ~3.1k stars, pushed 2026-10-31 — self-hosted reverse proxy turning Windsurf/Devin cloud model catalogs into OpenAI-, Anthropic- and Gemini-compatible APIs for Claude Code/Cline/Cursor.
- **Infisical/agent-vault** ([repo](https://github.com/Infisical/agent-vault)), ~2.5k stars, pushed 2026-10-27, license NOASSERTION (flag) — a Go "HTTP credential proxy and vault for AI agents like Claude Code, OpenClaw…": not a model gateway, but the *key-injection for harnesses* piece (adjacent to the launcher's `ISecureKeyStore`).

## Comparison table (2026-10-31)

| Tool | Protocol in / out | Self-host | Harness injection | License | Status |
|---|---|---|---|---|---|
| opencodex ([repo](https://github.com/lidge-jun/opencodex)) | In: `/v1/responses` + `/v1/chat/completions` + `/v1/messages`; Out: 40+ providers translated (OAuth or key) | Yes (npm CLI/service/desktop beta/Docker, `:10100`) | **Yes — native config for Codex/Claude Code/Claude Desktop/Grok Build + managed-file writes for ~25 clients (OpenCode, Pi, Kilo Code, Kimi CLI…) with restore** | MIT | Very active (pushed 2026-10-31); single-maintainer flag |
| CC Switch ([repo](https://github.com/farion1231/cc-switch)) | In: built-in Local Routing converts Anthropic Messages ↔ OpenAI Chat ↔ Responses ↔ Gemini; Out: 90+ presets | Yes (Tauri desktop app only) | **Yes — config-file writing for 12 tools incl. OpenCode/Pi/Kimi Code, automatic pre-write backup, coexist + switch modes, MCP/Skills sync** | MIT | Very active (~150.6k stars, pushed 2026-10-31) |
| copilot-api ([repo](https://github.com/ericc-ch/copilot-api)) | In: OpenAI + Anthropic (`/v1/messages`, count_tokens); Out: GitHub Copilot only | Yes (npx/Docker, `:4141`) | Yes — `--claude-code` generates launch command (env recipe); Claude Code only | MIT | Moderate (pushed 2025-11); ToS-risk flag |
| ccNexus ([repo](https://github.com/lich0821/ccNexus)) | In: Claude/OpenAI Chat/Responses/Gemini converters; Out: configured endpoints + Codex token pools | Yes (Wails GUI or headless HTTP `:3000`, Docker) | No — manual `~/.claude/settings.json` / `~/.codex/config.toml` docs | MIT | Active (~1.0k stars) |
| anyclaude ([coder](https://github.com/coder/anyclaude)) | In: Anthropic → Out: AI SDK providers (OpenAI/Google/xAI) | Yes (npm wrapper) | Wrapper launches Claude Code with env set (Claude Code only) | MIT | Stale (pushed 2025-08) |
| Agent Router, ex-Envoy AI Gateway ([repo](https://github.com/theagentrouter/agent-router)) | In: OpenAI-compatible (`aigw run` → `:1975/v1`); Out: Anthropic/Bedrock/Vertex/etc. via Envoy | Yes (standalone CLI or K8s/Envoy) | None | Apache-2.0 | Active (~2.3k stars); CLI experimental |
| gpt-load ([repo](https://github.com/tbphp/gpt-load)) | In: API-key/subscription clients; channel groups for OpenAI/Anthropic/Gemini; Out: pooled upstream keys | Yes (Go binary + web admin + DB) | None | MIT | Very active (~7.2k stars) |
| claude-code-proxy ([fuergaosi233](https://github.com/fuergaosi233/claude-code-proxy)) | In: Anthropic `/v1/messages`; Out: OpenAI-compatible | Yes (FastAPI/Docker, `:8082`) | Env recipe only | MIT | Slowing (pushed 2026-03) |
| bedrock-access-gateway ([aws-samples](https://github.com/aws-samples/bedrock-access-gateway)) | In: OpenAI-format (gateway); Out: Bedrock | Yes (Lambda/Fargate — cloud-shaped) | None | MIT-0 | **Archived/deprecated** — Bedrock now natively serves OpenAI + Anthropic APIs |
| sub2api ([repo](https://github.com/Wei-Shaw/sub2api)) | In: Anthropic `/v1/messages`, OpenAI `/v1/responses` (+WS), `/v1/chat/completions`; Out: OAuth/API-key account pools (Claude/OpenAI/Gemini/Grok/TypeSafe) | Yes (server + Postgres + Redis) | Partial — admin "Use Key" modal generates Grok CLI config file + OpenCode tab; others manual | **LGPL-3.0** | Very active (~45.2k stars); ToS/non-commercial flags |
| antigravity-claude-proxy ([repo](https://github.com/badrisnarayanan/antigravity-claude-proxy)) | In: OpenAI/Anthropic-compatible for Claude Code/OpenClaw; Out: Antigravity accounts | Yes | Unknown beyond stated targets | MIT | Active |
| WindsurfAPI ([repo](https://github.com/dwgx/WindsurfAPI)) | In: OpenAI-, Anthropic-, Gemini-compatible; Out: Windsurf/Devin cloud models | Yes | Unknown beyond stated targets | MIT | Active |

## Observations added on top of the earlier sections

1. **The harness-injection layer is now more competitive than the 2026-10-30 survey suggested, not less.** opencodex (native wiring + managed config writes for ~25 clients), CC Switch (12 tools, automatic config backup, coexist mode) and CCR's profiles all sit at layer 2. Any launcher delta has to be claimed in *shape* (one-shot CLI, per-model protocol declarations, OS key store) rather than in the injection capability itself, which is now table stakes on three projects. [opencodex client support](https://opencodex.me/reference/client-support/), [cc-switch README](https://github.com/farion1231/cc-switch)
2. **The strongest substitute is newer and faster-moving than CCR** (opencodex, June 2026 → 18k stars), and the largest substitute dwarfs everything else in adoption (CC Switch, ~150k stars) — but both are GUI/control-plane shaped, so "single thin `serve`-command launcher" packaging remains distinct.
3. **Service/lifecycle patterns to borrow or test against:** `ocx stop` restoring the pre-proxy config (opencodex), CC Switch auto-backup-before-rewrite + write-back on quit, copilot-api's clipboard launch-command generation. All three echo HarnessGateway's BackupService/restore-on-exit design — for the launcher this would not be reinvention but the same pattern plus user-visibility.
4. **The archive trend repeats at the provider level:** bedrock-access-gateway was archived because AWS added native OpenAI/Anthropic APIs, following y-router's archive after OpenRouter's "Anthropic Skin" — local translation proxies keep getting squeezed from below (providers going native) and above (gateways), strengthening the test-before-build recommendation.
