# ADR-0007: FreeLLMAPI as an optional, hardened cloud gateway, with an ALDREN Cloud Policy Layer

- **Status:** Accepted (2026-10-31); S8 go recorded in [ADR-0012](0012-gateway-go-chains-only.md) (2026-11-01), which also drops pinned models
- **Supersedes:** the ALDREN-built multi-provider Model Hub from the v1 plan

## Context
ALDREN needs optional access to many cloud providers, with routing, failover, quota tracking and usage visibility. FreeLLMAPI (MIT licence, github.com/tashfeenahmed/freellmapi) already provides this as an OpenAI-compatible gateway over ~34 free-tier providers: named fallback chains, a per-key quota ledger, health checks, failover, encrypted key storage, attribution headers and a usage API.

Its defaults are unsafe for ALDREN:
- keyless community and anonymous providers (including AI Horde's volunteer workers) work out of the box;
- its generic `auto` strategies rank every enabled model;
- its failover budgets (45 s, 90 s) are tuned for coding agents, not voice;
- it is a pre-1.0, single-maintainer project with near-daily releases.

## Decision
- **Reuse, don't rebuild:** FreeLLMAPI handles provider connectors, routing within a chain, failover, quotas, health checks, key storage and analytics.
- **Deployment:** headless, as a child process of ALDREN Core (pinned release, pinned portable Node), loopback only, with the hardening in [SECURITY.md](../SECURITY.md#freellmapi-hardening-applied-by-aldren-at-launch). ALDREN can alternatively connect to an existing instance by URL and key.
- **ALDREN keeps a thin Cloud Policy Layer:**
  - role → chain mapping (`aldren-fast`, `aldren-reason`, `aldren-vision`, `aldren-research`);
  - privacy classes and the egress gate, and consent;
  - client-side deadlines;
  - verification of `X-Routed-Via` against each chain's allowlist;
  - cost estimates for paid keys.
- **Direct paths:** local inference never goes through the gateway. ALDREN keeps direct paths only for the local llama-server, for optional Gemini-native grounded answers (the gateway appears to drop grounding sources), and for search APIs if the gateway's search layer can't be called directly.
- **Settings:** ALDREN shows status, chains, allowlists, consent rules, budgets, attribution and usage. Key, provider and chain management stays in the gateway's dashboard. A one-time setup wizard uses its admin API, then drift is checked (read-only) at every start.
- **Optional:** without the gateway, cloud features are unavailable and everything local keeps working.

## Alternatives considered
- **ALDREN-built Model Hub with direct adapters:** full control, but months of duplicated work. Kept as the fallback if S8 fails.
- **FreeLLMAPI desktop app:** fine for evaluation, but heavier (Electron), self-updating and with its key file on disk.
- **Docker or a Windows Service:** too heavy, or the wrong account context.
- **Deep admin-API integration now:** it would break on frequent upstream changes.

## Consequences
- A much smaller Phase 6, but a dependency on an external project that must be pinned, reviewed and upgraded deliberately.
- Free-tier capacity is variable; the aggregate "7.4B tokens/month" is not personal capacity.
- Upstream terms still apply: one account per provider and no quota multiplication; Cohere excluded; NVIDIA evaluation-only; Gemini's business-purpose clause noted.

## Revisit if
S8 finds unacceptable overhead, unreliable tool calling or a security problem, or the project becomes unmaintained.
