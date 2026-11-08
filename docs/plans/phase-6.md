# Phase 6 plan: Cloud gateway and Research Brain

Status: approved 2026-11-08 ("commit and push and move on to phase 6"; downloads allowed). Short record; acceptance afterwards.

## Downloads and system changes
| Item | Why | Pin |
|---|---|---|
| Portable Node.js 24.22.2 LTS (win-x64 zip) in `E:\AldrenData\gateway\node-v24.22.2-win-x64` | Runs the pinned FreeLLMAPI 0.16.14 independently of the system Node: the gateway supports Node 20.18–24 and its SQLite module is built for Node 24 (a first try with Node 26.13.0 failed on that and went to the Recycle Bin) | SHA-256 `ca1bff5d…e7799e2` from nodejs.org SHASUMS256.txt, verified |
| NuGet `AngleSharp` 1.10.0 | Extracting the main text of fetched web pages | lock file |

No Windows settings change. The gateway's encryption key moves to Credential Manager (`Aldren/gateway-encryption-key`, generated on first start); its database stays in `%LOCALAPPDATA%\Aldren\gateway`. The S8 database and key were lost with the reinstall, so the gateway starts fresh: **you add provider keys and a search key again in its dashboard** (Blocked until then).

## What gets built
| Piece | Where | Notes |
|---|---|---|
| Gateway supervisor | `Aldren.Cloud` | Starts FreeLLMAPI with the pinned Node in Core's kill-on-close Job Object, 127.0.0.1 and a random port, the SECURITY.md hardening environment plus 15 s provider timeouts; health check; restart with backoff; reads its unified API key from its own database (read-only) |
| Setup and drift check | `Aldren.Cloud` | Creates the four `aldren-*` chains empty if missing (contents are your choice in the dashboard); at every start reads chain contents and enabled keys (read-only) and blocks any chain with a provider outside the allowlist; cloud stays off while a key outside the allowlist is enabled |
| Cloud Policy Layer | `Aldren.Cloud` | Role → chain only (never bare model names); privacy classes: secrets never leave, sensitive content needs a per-request yes with a preview; first-token deadlines (fast 2.5 s, reason 10 s, vision 15 s, research 20 s); `X-Routed-Via` checked against the chain's providers (a mismatch blocks the chain and tells you); every request audited; a visible indicator |
| Research pipeline | `Aldren.Research` | Local query planning, the gateway's search (Wikipedia's public API as a keyless fallback), page fetching with size and time limits, private and loopback addresses refused, main-text extraction, numbered sources, synthesis on `aldren-research`, citation check (uncited sentences dropped), sources shown, short spoken summary |
| Research Brain | Core | Explicit wording ("look up", "research", "latest", "online") or a yes to "Want me to look that up online?"; reply with sources in the island and the conversation window |
| Quick Brain cloud fallback | Core | Off by default; when on, only when the local model is unavailable and the request carries nothing personal or sensitive; `aldren-fast` gets the utterance and persona only |
| Cloud page | App | Gateway status, chain check, "Open gateway dashboard", cloud on/off, the Quick Brain fallback switch |

Deferred: Gemini-native grounded answers (needs your Gemini key and a paid tier for Gemini 3), consent-gated cloud vision for the Action Brain (needs a preview of the exact screenshot), cost estimates (free tiers only so far), the overhead and failover measurements (need provider keys).

## Exit criteria (checked later, with you)
Cited answers typically in 10–15 s; simulated rate limits fail over within the chain only; a deliberately misconfigured chain is caught; with the gateway killed, ALDREN keeps working locally; no cloud call without an indicator and a log entry.
