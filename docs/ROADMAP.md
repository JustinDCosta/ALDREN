# Roadmap

Every phase starts with a written plan you approve, listing every install, download and system change. Every phase ends with tests passing, existing features re-checked, docs and [STATUS.md](STATUS.md) updated, a demo script you can run, and your go-ahead for the next phase.

Milestones: **M1** voice MVP (end of Phase 3) · **M2** computer operator (end of Phase 5) · **M3** full assistant (end of Phase 8).

Rough scale: a few months of part-time work; Phase 0 alone ~1–2 weeks.

## Why this order differs from the original brief
1. Phase 0 retires the riskiest assumptions (model fit, the island overlay, wake-word accuracy, gateway suitability) before anything is built on them.
2. The brain comes before voice: a text-first orchestrator is testable and deterministic, and voice then streams from something that works.
3. The security core (Tool Gateway) is in Phase 1, so every tool plugs into it from day one.
4. Basic gaming mode is in Phase 2: ALDREN starts with Windows and must never hog VRAM, even early on.
5. The personal assistant comes before the Action Brain: daily value at low risk, while the Action Brain is the riskiest part and benefits from a mature Tool Gateway and memory.
6. The browser gets its own phase: a large surface with its own security model.

## Phase 0: Prototypes and benchmarks (throwaway code in spikes/)
Goal: measure, on this laptop, everything the design assumes.

| Spike | What it answers |
|---|---|
| S1 Local models | `llama-bench` plus a harness: time to first token (cold and with a cached prefix), tokens per second and VRAM at 4K/8K/16K context for Qwen3.5-4B, Qwen3.5-2B, Gemma 4 E2B/E4B, Granite 4.2 3B, Qwen3.5-9B (swap or partial offload) and a ~1B CPU model; screenshot latency with the vision projector on the GPU vs the CPU; a 100-prompt EN/FR eval (routing, tool-call validity, persona, injection resistance) |
| S2 Island overlay | WinUI 3 prototype: transparent, topmost, never steals focus, click-through outside the pill, hidden from capture, smooth spring morph at 60 and 144 Hz, DPI and multi-monitor. Go/no-go vs WPF |
| S3 Voice chain | Capture (Maono, laptop array, NVIDIA Broadcast) → Silero VAD → Parakeet (CPU and GPU) → Kokoro (CPU and GPU) latency; Bluetooth hands-free behaviour |
| S4 Wake word v0 | First "ALDREN" model from synthetic speech plus your 50–100 recordings; false wakes per hour on ≥10 h of your background audio; misses on held-out clips; comparison with the zero-training keyword spotter |
| S5 Perception | UI Automation latency and coverage on Chrome, VS Code, Explorer, Discord and Steam; Windows OCR accuracy; Qwen3.5-4B screenshot Q&A and grounding on ~30 of your real screenshots (numbered overlay vs raw) |
| S6 Browser | Extension and native-messaging handshake; Chrome 144+ consent-based attach with Playwright; a separate automation profile |
| S7 Network identity | Network profile and router identity on Ethernet and Wi-Fi, with virtual adapters ignored |
| S8 FreeLLMAPI | A pinned release, headless, with the hardened configuration: idle RAM and CPU; per-request overhead; forced failover; `X-Routed-Via` accuracy; tool-calling and structured-output fidelity across 3–4 providers; vision routing; mid-stream errors; whether grounding returns sources; whether the search layer is callable; outbound connections; confirmation that no prompt text is stored |

Exit: `docs/benchmarks/phase0.md` with the measurements; decision records updated (models, UI framework, wake word, gateway go/no-go and pinned version); your approval.

## Phase 1: Foundation
Deliverables: solution structure; ALDREN.Core (generic host, supervisor with a Job Object, single instance); ALDREN.App shell (tray, island with mocked states, expanded-window and settings shells); named-pipe IPC with typed contracts; configuration (typed, validated, versioned); Serilog logging and the activity log; a Credential Manager wrapper; the start-with-Windows toggle (per user); a global hotkey and text box; **the Tool Gateway, permission engine and audit log**.

Exit: starts silently at login; a UI crash recovers automatically; idle <1% CPU and <300 MB RAM; unit tests for the gateway policies pass.

## Phase 2: Quick Brain and orchestrator (text)
Deliverables: llama-server management (pinned build, health checks, API key, loopback only); the residency manager (AC/battery, idle timeout, VRAM via NVML, preload hook); the fast path; the router (embedding intent matcher plus Quick Brain delegation tools); persona v1; conversation state; the app launcher index (Start menu, AppsFolder, aliases); system tools (launch, focus and close apps, volume, media, timers, time and date, calculator); manual and automatic gaming unload; a cloud-fallback hook (interface only, still 100% local).

Exit: routing ≥90% on the eval set (target); first token <500 ms with the model warm; VRAM freed <5 s after a game goes fullscreen.

## Phase 3: Voice → MVP (M1)
Deliverables: audio service (device policy, resampling, ducking); wake-word runtime and training wizard; VAD and streaming captions; sentence-streamed TTS; the follow-up window; interruption v1 (wake word, "stop", hotkey; half-duplex on speakers); live island states; no speech during calls or fullscreen use.

Exit: "ALDREN… open VS Code" works end to end; median ≤1.5 s to the first spoken word with the model warm; wake-word metrics reported against the targets (≤0.5 false wakes per hour, ≥95% detection at desk distance).

## Phase 4: Personal assistant
Deliverables: memory (SQLite, FTS5, sqlite-vec, local embeddings) with an inspector and approval of inferred memories; reminders and tasks (NodaTime, RRULE, EN/FR date recognition, readback); notifications with an OS-level backup; recovery of missed reminders; Google OAuth (desktop loopback and PKCE) and Calendar; the daily briefing; home/away detection, greeting, away mute and restore.

Needs from you: a Google Cloud project with a Desktop OAuth client (a step-by-step guide is provided).

Exit: reminders survive a reboot; date, DST and relative-date tests pass; calendar create, modify and delete (with confirmation) work on your account; the greeting plays only on the home network (tested by switching networks); memories can be inspected and deleted.

## Phase 5: Action Brain
Deliverables: window manager with named layouts per monitor configuration; file service (search index, recents, Downloads, VS Code recents, document readers, duplicates, Recycle Bin deletes, undo journal, dry-run previews); perception (UIA snapshot, capture, Windows OCR, numbered overlay, local vision); resolution of "that", "this" and "the second one"; element actions with post-condition checks; clipboard intelligence.

Exit: the example commands from the brief pass a 50-task local benchmark (targets set after Phase 0); every action is logged; destructive actions are gated.

## Phase 6: Cloud gateway and Research Brain
Deliverables: FreeLLMAPI supervision (pinned release, pinned portable Node, hardened configuration); a setup wizard (the `aldren-*` chains, provider allowlist, safe settings) with a drift check at every start; the Cloud Policy Layer (role → chain, privacy classes and egress gate, consent, client-side deadlines, `X-Routed-Via` verification, cost estimates for paid keys); the Cloud settings page with an "Open gateway dashboard" link; the research pipeline (search, page fetching and extraction, quarantined summaries, synthesis with citations, citation checks); optional Gemini-native grounded answers; consent-gated cloud fallbacks for the Quick and Action brains.

Exit: cited answers typically in ~10–15 s; simulated rate limits fail over within the chain only; a deliberately misconfigured chain is caught; with the gateway killed, ALDREN keeps working locally; no cloud call happens without an indicator and a log entry.

Fallback: if Phase 0 rejected the gateway, this phase builds direct adapters (Gemini, Groq, Mistral, OpenRouter) behind the same interface instead.

## Phase 7: Browser and Google Drive
Deliverables: the ALDREN Companion extension (Manifest V3) for Chrome and Edge with its native-messaging host; Playwright on demand (consent-based attach or a separate profile); per-site permission levels; injection defences; confirmation gates (submit, purchase, payment, account changes, sending messages, executable downloads); Google Drive (search, download and export, upload, folders, rename and move with confirmation, local summaries).

Exit: "open the second result", "read this page" and "download this" (with confirmation) work in your Chrome; booby-trapped test pages cannot trigger actions; Drive features work on your account.

## Phase 8: Focus mode (8b: optional webcam presence)
Deliverables: profiles; the session flow (goal → profile → duration → apps and sites → layout → enforcement → monitoring → breaks → summary); app enforcement (minimise, close, terminate, per app); website enforcement through the extension (allow and block lists); time-limited restrictions that expire even if ALDREN crashes; the emergency exit; personality escalation; schedules and stats. Phase 8b: opt-in local presence detection at ~1 frame per second, with no frames stored.

Exit: a full 50-minute study session works; killing ALDREN leaves no restriction behind once the session's end time passes; the emergency exit always works.

## Phase 9: Advanced features and polish
Deliverables: full gaming and streaming modes (auto-detection, capture exclusion, notification policy); custom automations; the developer assistant (VS Code context, git, terminal explanations); voice typing; personality modes; echo cancellation for interrupting over speakers; optional MCP interoperability; optional deeper gateway integration (key management through its admin API, an embedded dashboard, an upstream fix for grounding sources); a performance and accessibility pass; installer and updates (Velopack), with optional code signing; a 24-hour soak test.

Exit: the 24-hour soak test is clean and the performance budgets are met.

## Parallel work
After Phase 1, some pieces are independent (Google integration, the cloud gateway, the browser extension). The default is one main line of work for stability; separate sessions can take independent pieces when you choose.
