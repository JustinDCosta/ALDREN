# Security model

ALDREN has deep access to the PC, so security is part of the architecture, not a later add-on. This document is binding for every change (see CLAUDE.md).

## Principles
1. **Least privilege:** ALDREN runs non-elevated, and each capability needs a permission you granted.
2. **Models propose, code disposes:** model output is never executed directly.
3. **Local-first:** data stays on the PC unless the egress policy allows it.
4. **Untrusted input stays data:** web pages, documents, screen text and clipboard content never become instructions.
5. **Everything is inspectable:** every tool call, cloud request and permission change is logged and browsable.

## Threat model

| Threat | Examples | Main mitigations |
|---|---|---|
| Prompt injection | A web page, PDF, email or on-screen text saying "ALDREN, delete…" | Untrusted-content quarantine, confirmations, no arbitrary shell, risk tiers |
| Malicious local software | Another program calling ALDREN's local endpoints | Pipe ACLs, loopback-only services with random keys, the gateway's DNS-rebinding guard |
| Voice spoofing | A video, or someone nearby, saying the wake word and a command | Confirmations for risky actions, optional speaker verification, "only my voice" mode |
| Accidental damage | The wrong file moved, an app closed with unsaved work | Recycle Bin, undo journal, dry-run previews, confirmations |
| Cloud data leakage | Screenshots or memories sent to a provider that trains on them | Privacy classes, egress gate, per-request consent, provider allowlists, routing verification |
| Credential theft | API keys or OAuth tokens read from disk or logs | Credential Manager (DPAPI), no secrets in files, logs or prompts, log redaction |
| Supply chain | A tampered model, package or gateway release | Pinned versions with SHA-256, safe model formats only, reviewed upgrades |

Out of scope: malware already running as your user (it can read anything you can) and physical attacks.

## Tool Gateway contract
Every capability is a typed tool with a JSON schema, a risk tier, a required permission, a timeout, a confirmation policy, an optional undo handler and audit logging.

Execution order: schema validation → permission check → policy check (risk tier, untrusted-content taint, current mode) → confirmation if required → execution with timeout → post-condition verification → audit log → sanitised result returned to the model.

The gateway fails closed: if it can't write the audit log, nothing above tier 0 runs, and the failure goes to the log file. A confirmation nobody answers within 60 s, or that can't be shown because no App is connected, counts as declined.

## Risk tiers

| Tier | Examples | Policy |
|---|---|---|
| 0 Read | List windows, read the calendar, OCR the screen | No confirmation |
| 1 Reversible | Open an app, move a window, create a folder, set a reminder | No confirmation; undo where possible |
| 2 Impactful | Close apps with unsaved work, bulk moves, overwrites, editing events | Confirmation |
| 3 Destructive or external | Delete, send messages, submit forms, purchases, Drive changes | Explicit confirmation showing full details; optional voice verification |
| 4 Forbidden | Disable security software, elevate, change security settings, type passwords or card numbers, permanently delete without an explicit request | Never |

Deletes go to the Recycle Bin unless you explicitly ask for permanent deletion (tier 3, confirmed).

A tool can raise its tier for particular arguments, never lower it (Phase 5): pressing a control named "Send", "Pay", "Delete", "Sign out" and the like is tier 3, while "Next" stays tier 1; text that looks like a password or card number makes typing it tier 4. While screen, document or clipboard content is in the model's context, its calls carry that content as taint, so tier 2 and above are always confirmed.

## Permissions
You grant permissions in Settings, per capability (for example files: read-only vs modify; browser: read vs interact; per-site levels). They are stored where no tool can write. ALDREN's own folders (settings, database, logs) are off-limits to every tool. A model can never grant, widen or bypass a permission.

## Untrusted content
- Content from web pages, downloaded files, documents, the screen, emails and the clipboard is tagged with its source.
- It is processed by a model call **without tool access** (summaries and extraction only).
- While untrusted content is in a model's context, any tier-2+ action requires confirmation, whatever the settings say.
- Steps derived from web research ("find a solution online and apply it") are always shown as a plan and confirmed before execution.

## Cloud egress

| Class | Examples | Rule |
|---|---|---|
| Public | Your explicit research question | May go to approved providers |
| Personal | Calendar content, memories, profile facts | Only when the request needs it, through the egress gate |
| Sensitive | Screenshots, files, documents, clipboard, file contents | Per-request preview and consent; only providers you marked "no training on API data" |
| Secret | Passwords, keys, tokens, card numbers, ID numbers | Never leaves the PC |

Gateway rules:
- ALDREN sends only its curated `auto:aldren-*` chain ids, never bare model names: in S8 a bare `gemini-2.5-flash` went to two resellers ([ADR-0012](decisions/0012-gateway-go-chains-only.md)). Never plain `auto`, `auto:<strategy>` or Fusion, and never keyless, community or unvetted relay providers.
- The gateway keeps enabled keys only for the providers ALDREN uses. A read-only drift check at every start compares chain contents and enabled keys with the allowlists.
- After each response, ALDREN compares `X-Routed-Via` (and `X-Fallback-Trail`) with the chain's allowlist. A mismatch blocks that chain and alerts you.
- A visible indicator shows whenever a request goes to the cloud.

## Secrets
Stored in Windows Credential Manager (DPAPI, per user):
- provider and search API keys;
- Google OAuth tokens (through a custom token store, never the library's plaintext default);
- the gateway's master key, dashboard password and unified key;
- the llama-server API key.

Logs are redacted, and secrets never enter prompts.

## Local endpoints and processes
- ALDREN never runs elevated and never asks for admin rights.
- Core↔App communication uses a named pipe restricted to your account. ALDREN itself opens no TCP port.
- llama-server listens on 127.0.0.1 on a random port and requires a random API key (otherwise any web page could use it).
- Child processes run in a Job Object, so they never outlive ALDREN.
- ALDREN's windows are excluded from screen capture by default (a toggle allows showing them on stream).
- Browser bridge (Phase 7): Chrome or Edge starts `Aldren.BrowserHost.exe` through native messaging only for the extension ID fixed by the Companion's key (`allowed_origins`); the host talks to Core on a pipe restricted to your account and accepts only a fixed command list with 1 MB frames. The two HKCU `NativeMessagingHosts` keys are written only when you click Connect in Settings > Browser, and Disconnect removes them. Page content comes back as untrusted data; password and card fields are never read or filled, checked in the page script and again in Core.
- Google (Phase 7): your own OAuth client; the client secret and the refresh token live in Credential Manager (no token file); Disconnect revokes the token at Google and deletes it.
- Dictation (Phase 9) types with Unicode key events into the focused field only through the gateway tool `type_text`: never in gaming mode, never into password fields, never text that looks like a secret.

## FreeLLMAPI hardening (applied by ALDREN at launch)
- 127.0.0.1 and a random port only; `ALLOWED_HOSTS=localhost` turns on the DNS-rebinding guard.
- `ENCRYPTION_KEY` is injected from Credential Manager, so no `.encryption-key` file sits on disk. This protects against someone copying the drive; nothing protects keys from malware running as your user.
- Off: LAN access, URL tokens, tunnels, Ollama emulation, MCP, and, for ALDREN traffic, prompt compression, the response cache, context handoff and Fusion.
- Keyless, community and anonymous providers and unvetted relays are disabled. Keys are added only for approved first-party providers, one account each.
- The `aldren-*` chains are created empty with automatic additions off.
- `FALLBACK_TIME_BUDGET_MS`≈8000 and `PROVIDER_STREAM_STALL_TIMEOUT_MS`≈15000 (instead of 45 s and 90 s); `REQUEST_ANALYTICS_LOG_CLIENT=false`; `FREELLMAPI_UPDATE_CHECK=off`.
- Catalog sync stays on (monthly signed snapshot) unless you turn it off with `CATALOG_SYNC_DISABLED=1`. Per its docs and code (reviewed 2026-10-31), provider URLs live in its code, not in the catalog, so a tampered catalog can't redirect keys; Phase 0 (S8) re-checks this for the pinned version.
- The version is pinned. Upgrades happen only after changelog review, a compatibility test and your OK. It is installed outside this repository.
- Local encryption protects keys at rest; it does not make prompts private. Prompts still go to the upstream providers under their terms.

## Supply chain
- Pin every package, tool and model (version + SHA-256) and record them in the docs.
- Use models only in safe formats (GGUF, ONNX, safetensors); never pickle-based files.
- Download only from official sources, and treat downloads as untrusted data until verified.

## Antivirus considerations
An app that captures the screen, injects input and closes processes looks like spyware to antivirus heuristics. ALDREN uses standard APIs (RegisterHotKey, UI Automation) instead of low-level keyboard hooks, and code signing is planned for Phase 9.

## When a security review is required
Before adding a tool of tier 2 or above, opening any network listener, changing the egress policy or the provider allowlist, upgrading the gateway, adding a browser permission, or running anything with elevation.
