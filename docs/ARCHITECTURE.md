# ALDREN Architecture

> **Status:** approved design, version 2 (2026-10-31). Nothing is implemented yet; implementation state lives in [STATUS.md](STATUS.md).
> Related: [ROADMAP.md](ROADMAP.md) (phases) · [decisions/](decisions/README.md) (why each major choice was made) · [SECURITY.md](SECURITY.md) (binding security rules) · [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).
> Numbers marked *(est.)* are estimates that Phase 0 measurements will replace.

## Contents
1. Purpose and success criteria
2. Target environment and budgets
3. System overview
4. Processes and lifecycle
5. Orchestrator
6. The three brains
7. Local inference
8. Cloud access: FreeLLMAPI and the Cloud Policy Layer
9. Research pipeline
10. Voice
11. Screen understanding and Windows control
12. Browser automation
13. User interface
14. Personal assistant: reminders, tasks, memory, Google
15. Context awareness and modes
16. Focus mode
17. Security (summary)
18. Data, storage and retention
19. Observability
20. Testing strategy
21. Planned repository layout
22. Defaults adopted at approval
23. Key references

## 1. Purpose and success criteria

ALDREN is a local-first personal AI assistant for Windows 11, summoned by voice ("ALDREN", pronounced *al-drin*) or by text. It presents one JARVIS-like personality while three specialised "brains" handle conversation, computer operation and online research behind it.

Success means:
- **Trust:** nothing executes without passing the Tool Gateway; no surprise uploads, deletions or volume changes.
- **Speed:** simple requests answer in about a second; idle cost is negligible.
- **Local-first:** voice, chat, PC control, reminders and memory work offline.
- **Discretion:** it stays out of the way while you game or stream.

Current assumptions: one user, personal use, English-only speech ([ADR-0011](decisions/0011-supertonic-voice-english-only.md)).

## 2. Target environment and budgets

| Item | Value | Consequence |
|---|---|---|
| Machine / OS | Acer Nitro AN515-57, Windows 11 Home 25H2 | Windows-only APIs throughout |
| CPU | Intel i5-11400H, 6 cores / 12 threads, AVX-512 | Speech models and ≤2B fallback models run on the CPU |
| RAM | 16 GB dual-channel DDR4-3200; ~7.7 GB free during normal use | ALDREN ≈ 0.3–0.5 GB idle, ≤ ~1–2 GB active |
| GPU | RTX 3060 Laptop, 6 GB, compute capability 8.6, 95 W; ~770 MiB used by the desktop at idle | ≈ 4.5–5 GB for models: one resident model |
| Display | Usually docked: Dell S2340L 1080p60 driven by the NVIDIA GPU; internal 1080p144 panel | Layouts remembered per monitor configuration |
| Storage | C: ~24 GB free; E: NVMe with ample free space | Models, indexes and large data on `E:\AldrenData` |
| Audio | Maono PD300X (default input), laptop mic array, NVIDIA Broadcast virtual mic, Bluetooth headsets | Bluetooth headset mics never opened by default |
| Network | Ethernet, Wi-Fi, Tailscale VPN | Home detection by network identity; virtual adapters ignored |
| Locale | en-US interface, fr-FR installed, Europe/Paris | EN/FR understanding; DST-safe time handling |

Budgets (targets, verified in Phase 0 and guarded by tests where possible):
- Idle with the model unloaded: <1–2% CPU, ~300–500 MB RAM.
- Voice turn with the model warm: ≤1.5 s typical from end of speech to first audio; fast-path commands ~0.5–0.8 s.
- Screen analysis 2–4 s; research 5–15 s.
- Resident model ≤ ~4.6 GB VRAM including context.

## 3. System overview

```
ALDREN.App  (WinUI 3 · UI process)
  Dynamic Island · Expanded window · Settings · Tray · Confirmation dialogs
        ▲  named pipe (current user only) · typed RPC + streaming events
        ▼
ALDREN.Core  (.NET 10 · starts at logon · non-elevated · supervises children)
  INPUTS ──────────► ORCHESTRATOR ─────────────────────────────► OUTPUTS
  wake word→VAD→STT   fast path → router → planner → executor     island/UI events
  typed text, hotkey  → composer (one persona, one voice)         streamed TTS
  events (reminders,        │             │              │        notifications
  network, apps, focus)     ▼             ▼              ▼
                       QUICK BRAIN    ACTION BRAIN   RESEARCH BRAIN
                            └──────┬──────┘              │
                  Inference service (residency)   Cloud Policy Layer (role→chain,
                                   │              egress gate, consent, deadlines,
                                   │              routing verification)
  TOOL GATEWAY ─ schemas · permissions · risk tiers · confirmations · audit log
    ├─ Windows: apps · windows · files · UI Automation · capture · OCR
    ├─ Browser bridge     ├─ Memory (SQLite + FTS5 + vectors)   ├─ Reminders/tasks
    ├─ Google             ├─ Home/away   ├─ Focus   ├─ Audio (sherpa-onnx, in-process)
    └─ Settings · Secrets (Windows Credential Manager)
        │                          │                           │
        ▼ HTTP 127.0.0.1 + key     ▼ HTTP 127.0.0.1 + key      ▼ stdio (native messaging)
  llama-server (CUDA, GGUF)   FreeLLMAPI (optional, pinned)   Browser host ⇄ Companion extension
                                   │                           (+ Playwright/CDP on demand)
                                   ▼ HTTPS
                          Approved cloud providers
```

| Component | Module | Key technology |
|---|---|---|
| Desktop UI | Aldren.App | WinUI 3, Composition animations |
| Background runtime | Aldren.Core | .NET Generic Host, supervisor, Job Object |
| Voice: audio, wake word, VAD, STT, TTS | Aldren.Voice | WASAPI (NAudio), ONNX Runtime, sherpa-onnx |
| Orchestrator | Aldren.Core | fast path, router, planner, executor, composer |
| Quick / Action / Research brains | Aldren.Brains | role prompts and tool sets |
| Local inference | Aldren.Inference + llama-server | residency manager, NVML |
| Cloud access | Aldren.Cloud + FreeLLMAPI | Cloud Policy Layer, Microsoft.Extensions.AI chat client |
| Research | Aldren.Research | search adapters, page fetcher, citation checker |
| Windows automation, apps, files | Aldren.Windows | FlaUI (UIA3), CsWin32, Windows Search, IFileOperation |
| Screen understanding | Aldren.Perception | Windows Graphics Capture, Windows.Media.Ocr, vision model |
| Browser automation | Aldren.Browser + extension + host | Manifest V3, native messaging, Playwright for .NET |
| Memory and retrieval | Aldren.Memory | SQLite, FTS5, sqlite-vec, local embeddings |
| Google integrations | Aldren.Integrations.Google | official Google .NET clients, OAuth loopback + PKCE |
| Reminders, notifications, briefing | Aldren.Assistant | NodaTime, Ical.Net, Microsoft.Recognizers.Text, app notifications |
| Home/away and modes | Aldren.Presence | Network List Manager, Core Audio |
| Focus mode | Aldren.Focus + extension | window events, declarativeNetRequest |
| Permissions and security | Aldren.Security | Tool Gateway, policy engine, audit log, Credential Manager |

## 4. Processes and lifecycle

Decision record: [ADR-0003](decisions/0003-process-model.md).

| Process | Role | Started by | Notes |
|---|---|---|---|
| ALDREN.Core | Orchestrator, brains, services, Tool Gateway, audio | Per-user startup entry at logon, delayed 10–20 s | Non-elevated; single instance; supervises the others |
| ALDREN.App | Island, expanded window, settings, tray, confirmations | Core, hidden at start | Restarted by Core on crash; relaunches Core if Core dies |
| llama-server | Local inference (llama.cpp, CUDA) | Core, on demand | 127.0.0.1, random port, random API key |
| FreeLLMAPI | Optional cloud gateway | Core, lazily or at logon | 127.0.0.1, pinned, hardened (§8) |
| ALDREN.BrowserHost | Chrome/Edge native-messaging host | The browser | stdio only |
| Wake-word trainer, benchmarks | Offline tools (Python, isolated environment) | You, from Settings | Never part of the runtime |

- **IPC:** StreamJsonRpc over a named pipe restricted to your account; typed contracts in `Aldren.Contracts`; streaming events for tokens and state. ALDREN itself opens no TCP port.
- **Supervision:** child processes run in a Job Object (kill on close) so nothing outlives Core; crashes restart with exponential backoff.
- **Shutdown:** graceful on logoff and restart; reminders and focus leases are persisted.
- **Not a Windows Service:** services run in session 0, without the interactive desktop and audio context ALDREN needs.
- **Packaging:** development builds first; a per-user installer with updates (Velopack) and optional code signing in Phase 9.
- **Tray menu:** pause listening, gaming mode, focus, settings, quit, start with Windows on/off.

## 5. Orchestrator

### 5.1 Pipeline
Every stage can be cancelled and has a timeout.
1. **Normalise input:** transcript (with confidence) or typed text, plus a context snapshot: time, active window and app, current mode, home/away.
2. **Fast path (no model, <10 ms):** grammar and fuzzy matching for frequent commands (open, close or switch apps; volume; media; timers; time and date; screenshots; mode changes), executed directly through the Tool Gateway.
3. **Router:** a local embedding classifier (~20–50 ms *(est.)*) plus explicit triggers: "look up", "research", "online", "latest" → Research; "this", "that", "on my screen", "this error" → Action with screen context. Everything else goes to the Quick Brain, which answers or delegates by calling a tool (`delegate_to_action`, `request_research`); routing is a schema-validated tool choice.
4. **Policy check:** deterministic rules override the model. Research needs your explicit wording or a yes to "Want me to look that up online?"; sensitive data never leaves without consent; gaming mode forbids GPU-heavy paths.
5. **Planner:** for actions, a short typed plan (steps = tool calls with expected outcomes). Plans that change things are shown to you when their risk tier requires confirmation.
6. **Executor:** runs steps through the Tool Gateway, re-observes after each step (UIA/OCR) and re-plans on mismatch. Retries are capped and never silent.
7. **Composer:** streams the answer sentence by sentence to the UI and to speech, in ALDREN's persona.

### 5.2 Routing examples
| You say | Path |
|---|---|
| "How are you?" | Quick Brain |
| "Open Chrome." | Fast path → `launch_app` (no model) |
| "What is this error?" | Action reads the dialog via UIA/OCR; vision only if text extraction fails |
| "Arrange my desktop." | Action → layout tools; preview first for large rearrangements |
| "Research the latest GPUs." | Research Brain; cloud indicator on; sources shown |
| "Find a solution online and apply it." | Action extracts the error → Research → Action plan → your confirmation → execute and verify |
| "Explain this Python code on my screen." | Action reads the code (UIA/OCR) → explanation; nothing executed |
| "What do I have to do tomorrow?" | Calendar and tasks fetched in parallel → Quick Brain answers (works offline from cache) |

Independent lookups run in parallel (for example the daily briefing); dependent steps run in sequence (research → action).

### 5.3 Failures
Per-stage timeouts; cloud fallbacks follow §8. If the local model is unavailable (gaming, battery), ALDREN uses the CPU model or says what it can't do right now. Tool failures are explained in plain language with a link to the log entry. Destructive steps are never retried automatically.

### 5.4 Conversation
- Follow-up window of ~8 s after ALDREN speaks: no need to repeat the wake word.
- Context = a fixed prompt prefix (kept stable for prompt caching) + rolling summary + recent turns + retrieved memories, within a fixed token budget.
- Interruption: the wake word, "stop" or the hotkey cancels speech and generation immediately.

## 6. The three brains

| | Quick Brain | Action Brain | Research Brain |
|---|---|---|---|
| Job | Front door: chat, Q&A, maths, simple commands, deciding who handles what | Seeing and operating the PC: screen, windows, files, browser, multi-step plans | Current information from the web, comparisons, fact-checks |
| Model | Qwen3.5-4B (4-bit), reasoning mode off, resident on the GPU | The same resident weights + vision projector, an agent prompt and more context; escalates to Qwen3.5-9B on demand, or to `auto:aldren-reason` / `auto:aldren-vision` with consent | Cloud models via `auto:aldren-research`; optional Gemini-native grounding |
| Runs | Local (CPU fallback when the GPU is unavailable; optional cloud fallback, §8.4) | Local | Cloud, on explicit request only |
| Sees | Persona, profile summary, recent turns, relevant memories, active app, time, mode | All of that + structured screen state + tool results | Only the research question + context you approve |
| Tools | Fast tools + delegation tools | Windows, file and browser tools through the Tool Gateway | Search and page reading; no access to the PC |
| Typical latency | 0.2–0.5 s to first token *(est.)* | 1–4 s per step *(est.)* | 5–15 s *(est.)* |

One assistant:
- **One persona spec** (identity, tone, humour level, "boss", style examples) is injected into every local generation. Personality modes (professional, friendly, sarcastic, serious, concise, detailed) are overlays on it.
- **One context store** (conversation, profile, memories, active app, home/away, mode). Each brain gets only the slice it needs.
- **One voice:** everything you hear is composed locally. Cloud research returns findings and sources; ALDREN summarises them in its own voice and the UI shows the full answer.
- **Badges:** every reply shows which brain and model answered, local or cloud, and the latency.

Escalation ladder: Quick (4B) → Action (same 4B + vision + tools) → local 9B (swap) → cloud chain (only within the egress policy and your consent).

## 7. Local inference

Decision record: [ADR-0004](decisions/0004-local-inference.md).

**Engine:** llama.cpp `llama-server`, pinned release, CUDA build. Router mode (on-demand load and unload) or ALDREN-managed processes, chosen in Phase 0. Loopback only, random API key.

**Settings:** all layers on the GPU; flash attention; 8-bit KV cache; 8K context by default (16K when VRAM allows); JSON-schema-constrained output for routing and tool calls; a stable prompt prefix with cache reuse, with the warmed prompt state saved to disk and restored after reloads; vision projector on the GPU, or on the CPU under VRAM pressure (slower screenshots, ~0.7 GB saved); no speculative decoding.

**Lineup** (provisional until Phase 0 spike S1):

| Role | Model | Where | Memory *(est.)* |
|---|---|---|---|
| Quick + Action (resident) | Qwen3.5-4B Q4_K_M + vision projector | GPU | 3.8–4.6 GB VRAM |
| Deep local reasoning (on demand) | Qwen3.5-9B Q4_K_M (or Q3_K_M) | GPU, swapped in | ~5.5 GB VRAM |
| Gaming / battery fallback | LFM2.5-1.2B or Qwen3.5-0.8B | CPU | ~1 GB RAM |
| Embeddings (memory, intents) | EmbeddingGemma-300M | CPU (ONNX) | ~0.3 GB RAM |

Benchmarked alternatives: Qwen3.5-2B, Gemma 4 E2B/E4B, Granite 4.2 3B. GUI-grounding specialists (for example Step-GUI-4B) only if Qwen3.5's grounding proves insufficient.

**VRAM budget:** ~770 MiB desktop + ~300–450 MiB CUDA runtime leaves ~4.9–5.1 GB for weights and KV cache, less while a game, OBS or video playback is running.

**Residency policy** (Model Manager, VRAM monitored via NVML):

| Situation | Behaviour |
|---|---|
| Plugged in, no game, enough VRAM | Model warm on the GPU |
| Wake word heard while the model is cold | Start loading immediately (you're still speaking for 1–3 s) |
| On battery, idle | Unload (a live CUDA context keeps the NVIDIA GPU awake) |
| Fullscreen game, OBS, or VRAM pressure | Unload within seconds; CPU fallback model, or cloud fallback if allowed |
| Hard task flagged | Swap to the 9B (~2–4 s *(est.)*), swap back when idle |

**Screenshots:** cropped to the relevant window and downscaled before the vision model; UIA and OCR come first, so most turns need no image at all.

## 8. Cloud access: FreeLLMAPI and the Cloud Policy Layer

Cloud access is optional. Without it, cloud features are unavailable and everything local keeps working. Decision record: [ADR-0007](decisions/0007-cloud-gateway-freellmapi.md).

### 8.1 Gateway
- **What:** FreeLLMAPI (MIT, github.com/tashfeenahmed/freellmapi): one OpenAI-compatible endpoint over ~34 free-tier providers, with named fallback chains, a per-key quota ledger, health checks, failover, encrypted key storage, attribution headers (`X-Routed-Via`, `X-Fallback-Trail`) and usage endpoints (`/v1/usage`, `/v1/models?execution_status=ready`).
- **Deployment:** headless, started and supervised by Core: a pinned release run with a pinned portable Node runtime from `E:\AldrenData\gateway\<version>`, never inside this repository. ALDREN can instead connect to an existing instance (for example the FreeLLMAPI desktop app) by URL and key.
- **Hardening:** applied by ALDREN at launch; see [SECURITY.md](SECURITY.md#freellmapi-hardening-applied-by-aldren-at-launch).
- **Upgrades:** only after changelog review, a compatibility test and your approval.

### 8.2 Chains
Created empty, with automatic additions off, by the setup wizard; edited in the gateway dashboard.

| Chain | Purpose | Candidate providers (only those you approve) |
|---|---|---|
| `aldren-fast` | Fast cloud fallback | Groq, Cerebras |
| `aldren-reason` | Harder reasoning and planning | Gemini Flash, gpt-oss-120b on Groq or Cerebras, Mistral |
| `aldren-vision` | Optional image understanding | Gemini Flash; Llama 4 Scout only if a first-party provider offers it (in S8 only Cloudflare and a reseller did) |
| `aldren-research` | Research synthesis from public sources | Gemini Flash, larger free models via OpenRouter or Groq |

Default allowed providers: Gemini (your key), Groq, Cerebras, Mistral, OpenRouter, GitHub Models (first-party only). Excluded: keyless, community and anonymous providers, unvetted relays, and Cohere (its terms forbid personal use). NVIDIA NIM only if you accept its evaluation-only terms.

### 8.3 Cloud Policy Layer (ALDREN)
- **Role → chain mapping.** ALDREN sends only its curated `auto:aldren-*` chain ids, never a bare model name, which can resolve to a reseller selling a model by that name ([ADR-0012](decisions/0012-gateway-go-chains-only.md)). Never plain `auto`, `auto:<strategy>` or Fusion.
- **Drift check:** at every start, each chain's contents and the gateway's enabled keys are compared (read-only) with ALDREN's allowlists; cloud use stays off until a mismatch is fixed.
- **Privacy classes and egress gate:** see [SECURITY.md](SECURITY.md#cloud-egress).
- **Consent:** per request for sensitive content, with a preview of exactly what leaves the PC; per-session options where you choose them.
- **Deadlines** (first-token defaults, tuned in Phase 6): `aldren-fast` 2.5 s, `aldren-reason` 10 s, `aldren-vision` 15 s, `aldren-research` 20 s. On expiry, ALDREN cancels and falls back to local or tells you. The gateway runs with provider timeouts of about 15 s, so a hung provider fails over instead of holding a request for its 60 s default.
- **Routing verification:** after each response, `X-Routed-Via` and `X-Fallback-Trail` are compared with the chain's allowlist; a mismatch blocks the chain and alerts you.
- **Attribution and audit:** provider, model, failed attempts, latency and tokens are recorded per request and shown in the UI.
- **Cost:** the gateway counts tokens but doesn't price them. For paid keys, ALDREN estimates cost from token counts and a price table and enforces your budget.

### 8.4 Use by each brain
- **Quick:** local only, unless (1) the local model is unavailable, (2) the request carries nothing sensitive, and (3) you enabled the fallback. Then `aldren-fast` receives only the utterance, the persona prompt and at most the last few non-sensitive turns; a cloud badge shows; if no first token arrives within 2.5 s, ALDREN says so instead of waiting.
- **Action:** local only, unless the local vision model fails or is unsure and you accept a previewed upload (cropped to the relevant window, optional blur of detected text fields) to `aldren-vision`. Text descriptions of the screen may go to `aldren-reason`, also with consent. The cloud describes; it never acts: every action still goes through the local Tool Gateway.
- **Research:** see §9.

### 8.5 Direct paths kept outside the gateway
Local llama-server (always); optional Gemini-native grounded answers (keeps source metadata, which the gateway appears to drop); direct search adapters if the gateway's search layer isn't callable (§9).

### 8.6 Settings ("Cloud" page)
ALDREN shows gateway status, version and catalog version; the role → chain mapping and each chain's allowlist; consent rules; budgets; a live request feed (provider, model, failed attempts, latency, tokens); monthly usage and chain readiness. "Open gateway dashboard" opens FreeLLMAPI's own dashboard (127.0.0.1) for provider keys, provider and model toggles, chain editing, analytics, logs, search engines and backups. A one-time setup wizard uses the gateway's admin API to create the chains, disable keyless providers and apply safe settings; at every start ALDREN re-reads the configuration (read-only) and warns you about drift.

### 8.7 Fallback plan
If Phase 0 rejects the gateway, ALDREN uses direct adapters (Gemini, Groq, Mistral, OpenRouter) behind the same OpenAI-compatible client interface; the rest of this design is unchanged.

## 9. Research pipeline

Decision record: [ADR-0008](decisions/0008-research-pipeline.md).

1. **Trigger:** your explicit request, or a yes to "Want me to look that up online?".
2. **Plan:** turn the question into a few search queries (locally when possible).
3. **Search:** the gateway's search layer if Phase 0 shows it returns structured results; otherwise direct adapters: Tavily (1,000 free credits/month), Brave, Exa, or a self-hosted SearXNG.
4. **Read:** ALDREN fetches only the pages it needs, with size limits and an honest user agent, extracts the main text and tags it untrusted.
5. **Summarise sources:** a model call without tool access summarises each page.
6. **Synthesise:** `auto:aldren-research` writes the answer with numbered citations.
7. **Verify:** every claim must cite a fetched source; uncited claims are flagged or removed; contradictions between sources are shown.
8. **Deliver:** the UI shows the full answer, its sources and the provider/model used; ALDREN speaks a short summary composed locally.

Optional quick grounded answers use a direct Gemini adapter that keeps source metadata (needs your Gemini key; Gemini 3+ grounding needs the paid tier). "Find a solution and apply it" always ends in a plan you confirm (§5.2).

## 10. Voice

Decision record: [ADR-0005](decisions/0005-voice-stack.md).

**Flow:** microphone (WASAPI, 16 kHz) → ring buffer → wake word (CPU, always on) → chime, island and model preload → Silero VAD end-pointing → streaming STT with live captions → orchestrator → TTS sentence by sentence (other audio ducked) → follow-up window → back to wake word only.

**Wake word:** an openWakeWord-style model for "ALDREN":
1. Synthetic positives from many TTS voices, with respellings (Aldren, Al-drin, All-drin), speeds and pitches.
2. 50–100 of your recordings, captured with a Settings wizard (normal, tired, loud, across the room, with music or a game playing), plus a held-out set for testing.
3. Negatives: generic speech and noise corpora, near-misses ("Aldrin", "Alden", "all then", "Darren", "children") and an hour of your own background audio.
4. Augmentation with your microphones' characteristics, reverb and noise; training on your GPU *(est. 30–90 min)*.
5. Evaluation: false wakes per hour on ≥10 h of your background audio; misses on held-out clips at 0.5–3 m; threshold choice.

Targets: ≤0.5 false wakes per hour and ≥95% detection at desk distance, measured rather than promised. Runtime <1% CPU, a cooldown after each trigger, a sensitivity slider with a live meter, an optional voice-specific verifier and an optional second-stage transcript check (~150 ms).

**Speech-to-text:** Parakeet-TDT-0.6B-v3 (int8) via sherpa-onnx, on the CPU by default (~0.2–0.5 s per utterance *(est.)*), GPU optional. Larger models (Qwen3-ASR-1.7B, Granite Speech 4.1) are candidates for dictation later.

**Text-to-speech:** Supertonic 3 (int8) via sherpa-onnx on the CPU: ~0.2 s to first audio for a short sentence (measured in S3). Replies are split into sentences and spoken one at a time, first sentence first, in Supertonic's preset voice 5 ([ADR-0011](decisions/0011-supertonic-voice-english-only.md); Kokoro took ~0.7–2.9 s on the CPU). You want a more natural voice than any preset, so Phase 3 evaluates more natural engines, GPU ones included. No real person's voice is cloned without rights.

**End of speech:** 400 ms of silence ends a turn; recognition starts speculatively at 250 ms and is discarded if you keep talking, and 300 ms of audio from before the VAD fires is kept so first sounds aren't clipped.

**Audio policy:**
- Input priority: Maono PD300X, then the laptop array. Bluetooth headset microphones only if you choose them (they force low-quality hands-free audio). The NVIDIA Broadcast mic is optional; the wake word is validated on whichever input chain you use.
- Other apps' audio is ducked while ALDREN speaks.
- No speech during calls (microphone in use by another app), fullscreen games or meetings: visual responses instead.
- The Windows microphone indicator stays on while listening; the tray can pause listening.

**Interruption:** natural with headphones. On speakers, v1 mutes the mic while ALDREN speaks, except for "stop" and the hotkey; echo cancellation arrives in Phase 9.

**Languages:** English only (your decision, 2026-11-01). Some French may be understood, but it is not supported.

## 11. Screen understanding and Windows control

**Perception ladder** (cheapest first; stop as soon as there is enough):

| Level | Source | Cost *(est.)* | Gives |
|---|---|---|---|
| 1 | Win32 and shell metadata | <5 ms | Active app, window titles, monitors |
| 2 | UI Automation tree (FlaUI/UIA3) | 50–500 ms | Controls: names, roles, states, values, bounds, patterns |
| 3 | Windows Graphics Capture + Windows OCR (EN/FR) | 100–300 ms | Text where UIA sees nothing |
| 4 | Numbered overlay | +50 ms | Boxes over UIA/OCR elements so the model answers "#14", not pixel coordinates |
| 5 | Local vision model | 1.5–4 s | Layouts, icons, "what am I looking at?" |
| 6 | Cloud vision | — | Only with per-request consent (§8.4) |

This hybrid follows the approach of Microsoft Research's UFO² Windows agent: UIA first, visual grounding for the gaps, and UIA checks before acting.

- **Capture:** the Windows capture border stays visible by default as a "looking" indicator; ALDREN's own windows are excluded from capture.
- **Actions** (typed): launch, focus or close an app; move, resize or snap a window; apply a layout; invoke an element; set text; select an item; scroll; allowlisted keyboard shortcuts; file operations; screenshot. Each is validated, permission-checked, confirmed when required, executed with a timeout and verified by re-observing. Elements are referenced by IDs from the latest snapshot; stale IDs are rejected. Pixel clicks are a flagged last resort after a fresh screenshot check, disabled in sensitive apps.
- **References:** "that" = the element under the mouse cursor or the focused/selected element; "the second result" = reading order in the current list; ALDREN asks when ambiguous.
- **Limits:** no control of elevated windows or UAC prompts (by design); never any input into games.
- **Windows:** monitor topology, DPI and work areas; named layouts ("Coding": VS Code 60% left, Chrome 40% right, Terminal bottom-right; "Study"; "Streaming": game, OBS, chat) remembered per monitor configuration; minimised and maximised states restored; missing apps launched; virtual displays (Apollo, screen sharing) ignored unless in use.
- **Files:** Windows Search index, Recent items, Downloads, VS Code recent workspaces; readers for PDF (PdfPig), DOCX (Open XML SDK), text and code; duplicates found by size then hash; moves and renames journaled with undo; deletes go to the Recycle Bin (IFileOperation); bulk or overwriting operations show a dry-run preview and need confirmation; system folders are denied.
- **Clipboard:** change notifications without polling; processed only when you ask; treated as sensitive; password managers' "do not monitor" clipboard formats respected.

## 12. Browser automation

Decision record: [ADR-0009](decisions/0009-browser-automation.md).

| Channel | Use |
|---|---|
| A. ALDREN Companion extension (Manifest V3, Chrome and Edge) + native-messaging host | Default: tab context, page reading, clicks and form filling, tabs, downloads, focus-mode blocking |
| B. Playwright for .NET (CDP), on demand | Complex flows: attach to your running Chrome via Chrome 144+ consent-based remote debugging, or use a separate ALDREN automation profile |
| C. UI Automation | Other browsers: URL, title, simple controls |

Safety: page content is untrusted; per-site levels (read, interact, forms); confirmations before submitting, buying, paying, changing accounts, sending messages or downloading executables; ALDREN never types passwords or card numbers; a visible "ALDREN is acting on this tab" indicator; every action logged.

## 13. User interface

Decision record: [ADR-0002](decisions/0002-winui3-ui.md). WinUI 3 (Windows App SDK 2.x), with WPF as the fallback after the Phase 0 spike.

**Island:**
- A near-black pill (like the real Dynamic Island) with a 1 px highlight and an accent light ("arc-reactor" cyan, configurable).
- Top-centre of the active monitor; collapsed ~180×36 px, listening ~420×100, answers, sources and confirmations up to ~560×380.
- Spring morphs of ~300–400 ms on the Composition layer; respects the Windows "Animation effects" setting.
- A pre-created hidden window: topmost, never takes focus (WS_EX_NOACTIVATE) unless you click into it, click-through outside the pill, auto-collapse after idle; hidden from screen capture by default, with a "show ALDREN on stream" toggle.
- States: listening (waveform from mic level), transcribing (streaming text), thinking (shimmer), acting (live step list), researching (source icons appear), speaking (waveform + captions), confirm (large Yes/No, or say "yes"), error.
- Keyboard: Esc dismisses, Enter sends, Ctrl+Enter expands.

**Expanded window:** Mica backdrop; conversation with brain, model and cloud badges; source cards; task progress; a collapsible "What I did" log with Undo where possible; text and microphone controls.

**Settings sections:** General · Voice & wake word · Local models · Cloud (FreeLLMAPI) · Permissions · Privacy & data · Memory · Reminders & tasks · Google account · Focus mode · Home & away · Modes · Automations · Activity history · Performance · Diagnostics.

**Design tokens:** Segoe UI Variable and Cascadia Code; 4/8 px spacing grid; corner radii 8 (controls), 12 (cards) and pill (island); motion of 120–180 ms for micro-interactions and 250–400 ms for shape changes; dark-first, with light and high-contrast themes; full keyboard and Narrator support; optional subtle earcons.

**Hotkeys:** registered with RegisterHotKey (no low-level keyboard hooks); configurable keys for the text box and push-to-talk.

## 14. Personal assistant: reminders, tasks, memory, Google

### 14.1 Reminders and tasks
- SQLite is the source of truth and the Core scheduler fires reminders. Reminders missed during sleep or a reboot are shown on return ("While you were away…").
- The model extracts intent and slots; dates are resolved deterministically (Microsoft.Recognizers.Text, EN and FR) against the actual local time. NodaTime handles IANA time zones and DST; recurrence uses RFC 5545 RRULE (Ical.Net). One-time reminders are stored as UTC instants plus their time zone; recurring ones as local wall-clock time plus zone.
- Ambiguous expressions ("at 7", "next Friday", "tomorrow" said after midnight) are read back with the resolved date and time before saving. Nothing is silently guessed.
- Delivery: a Windows notification, optional speech, snooze; an OS-level backup notification so a reminder still appears if ALDREN isn't running (mechanism validated in Phase 4); optional Google Calendar or Tasks sync.
- Tasks: lists, due dates, priorities and projects; a task page in the expanded window; create, update and complete by voice.

### 14.2 Memory
- Types: profile facts, preferences, projects, routines and procedures (for example window layouts), episodic conversation summaries.
- Writes: "remember that…" saves immediately; memories ALDREN infers wait for your approval; passwords, keys, card numbers and ID numbers are blocked by detectors; health and financial details are stored only if you explicitly ask.
- Retrieval: hybrid keyword (FTS5) + semantic (sqlite-vec with local EmbeddingGemma embeddings) + recency, within a small token budget.
- Control: a memory inspector (search, edit, delete, export), "forget that", retention for raw conversation logs (90 days by default; summaries kept longer), optional database encryption (SQLCipher with a DPAPI-protected key).
- Memory never goes to the cloud, except specific facts a request needs, through the egress gate.

### 14.3 Google Calendar and Drive
- You create a free Google Cloud project, enable the Calendar and Drive APIs and create a "Desktop app" OAuth client (step-by-step guide in Phase 4). The consent screen is published "In production" (unverified, up to 100 users) because "Testing" expires refresh tokens after 7 days.
- Sign-in uses the installed-app flow with a loopback redirect and PKCE (official Google .NET libraries). Tokens are kept in a custom Credential Manager/DPAPI store, never the library's plaintext default. Settings shows the connection status and lets you re-authorise, disconnect or revoke (Google's revoke endpoint plus a local wipe).
- Least privilege with incremental consent: Calendar events (plus calendar list read); Drive starts with metadata read, then read-only for reading and summaries, then full access only when you enable Drive organisation (Google shows an "unverified app" warning for these scopes).
- Calendar: agenda, free/busy conflict checks, create and modify with readback, delete after confirmation; incremental sync every few minutes (push notifications would need a public server).
- Drive: search, download and export (Google Docs to PDF, DOCX or text), resumable upload, folders, rename and move (bulk changes confirmed), local summaries (cloud only with consent).

### 14.4 Daily briefing
On request, or after the home greeting on the first login of the day: date, weather (Open-Meteo, no key needed), calendar, due reminders and tasks, a focus suggestion. A spoken summary of at most ~45 s plus a visual card.

## 15. Context awareness and modes

### 15.1 Home and away
Decision record: [ADR-0010](decisions/0010-home-away-detection.md).
- Home = a trusted network fingerprint (Network List Manager profile + router hardware address, stored hashed; the Wi-Fi name when available) on any physical adapter. An unknown network = Away; no network yet = Unknown. Virtual adapters are ignored.
- Checked at startup, on network change, on resume and on unlock, with a 20 s grace period.
- "Welcome back, boss." plays only when Home is confirmed, on the first login or unlock after at least 4 hours (configurable), and not during calls, fullscreen games or meetings (then the greeting is visual only).
- Away mute: on Away, or on Unknown after the grace period (configurable, on by default), ALDREN saves the volume and mute state, then mutes; skipped when the output is headphones (configurable). The volume is restored on return home only if ALDREN did the muting and you haven't touched the volume since.
- A manual override ("ALDREN, I'm home", or the tray) lasts until the next network change. Each action is a separate switch.

### 15.2 Modes

| Mode | Entered when | Behaviour |
|---|---|---|
| Normal | Default | All features |
| Study / Work | Focus session or manual | Focus rules (§16); notifications filtered |
| Gaming | Fullscreen game detected, or manual | GPU models unloaded; no island over exclusive fullscreen (voice plus a small indicator); no screen capture; notifications deferred except urgent reminders; wake word stays on |
| Streaming | OBS running, or manual | Gaming behaviour, plus ALDREN hidden from capture unless "show on stream" is on, and no personal information on screen |
| Away | See §15.1 | Optional mute; no greeting; speech only through headphones (configurable) |
| Silent | Manual | No speech; text only |

Detection uses Windows' user notification state (fullscreen or busy), known game library folders, per-process GPU use (NVML) and the OBS process. ALDREN never injects input into games.

### 15.3 Context awareness
The active app and the current mode shape suggestions and tool selection (for example coding help when VS Code is focused). Detection is cheap (window events), and none of it leaves the PC.

## 16. Focus mode

**Flow:** you state the goal → ALDREN picks or creates a profile (asking only if unclear) → agrees a duration (50/10 by default) → opens apps and sites → applies the layout → enforces → monitors → breaks → wrap-up with stats.

| Layer | Mechanism | Strength |
|---|---|---|
| Nudge | Persona warnings when a blocked app or site comes to the front | Soft |
| Apps | Window events (no admin needed): minimise, then close, then terminate, escalating per app | Medium |
| Websites | Companion extension allow/block lists (declarativeNetRequest) in Chrome and Edge; other browsers' windows nudged or closed | Medium to strong |
| Strict (optional, later) | Browser policies or system-level blocking through an elevated helper: opt-in, documented, reverts automatically | Strong |

- Every restriction is a lease with an end time. The extension expires its own rules even if ALDREN crashed, and Core reverts leftovers at startup.
- The emergency exit (phrase, hotkey, tray) always works. No Windows services are touched and nothing is irreversible.
- Personality: gentle, then sarcastic, then dramatically disappointed, with a serious mode; the first slip is forgiven; celebration and streaks at the end; never insulting.
- Schedules come from the calendar or fixed times; profiles are editable in Settings.
- **Webcam presence (Phase 8b, optional):** off by default and opt-in per session; processed locally at ~1 frame per second on the CPU; only present, absent or roughly looking away; no frames stored or uploaded; a visible indicator; instant off; never active outside focus sessions. Presence is not attention, and it only works with the lid open.

## 17. Security (summary)

The binding rules are in [SECURITY.md](SECURITY.md) and [ADR-0006](decisions/0006-tool-gateway-security.md). In short: models propose and the Tool Gateway disposes; five risk tiers; permissions that only you can grant; untrusted content processed without tools; no arbitrary shell; no elevation; loopback-only services with authentication; secrets in Credential Manager; cloud egress only through the Cloud Policy Layer with routing verification; everything audited.

## 18. Data, storage and retention

| Data | Location | Protection | Retention (default) |
|---|---|---|---|
| Settings | `%APPDATA%\Aldren` (JSON, versioned schema) | No secrets inside | Until changed |
| Secrets (API keys, OAuth tokens, gateway master key and passwords, llama-server key) | Windows Credential Manager | DPAPI, per user | Until removed |
| Main database (memory, reminders, tasks, conversations, activity and audit log) | `%LOCALAPPDATA%\Aldren\aldren.db` | Owner-only ACL; optional SQLCipher | Raw conversations 90 days; audit log 180 days |
| Logs | `%LOCALAPPDATA%\Aldren\logs` | Redacted, rolling | 14 days |
| Gateway database | `%LOCALAPPDATA%\Aldren\gateway` | Gateway's AES-256-GCM key store | Gateway defaults (analytics 90 days) |
| Models | `E:\AldrenData\models` | Pinned SHA-256 | Until removed |
| Gateway code | `E:\AldrenData\gateway\<version>` | Pinned release | Previous version kept for rollback |
| Wake-word recordings | `E:\AldrenData\voice` | Never uploaded or committed; deletable from Settings | Until you delete them |
| Benchmark data | `E:\AldrenData\bench` | Local only | Until removed |

## 19. Observability
- Structured logging with Serilog, with secrets redacted.
- One trace per request: stage timings, route, models, tokens, tools called, permissions checked and cloud egress, shown in Activity history.
- Resource monitoring: CPU and RAM per process, VRAM via NVML.
- The Phase 0 benchmark harness becomes a regression suite for latency and resource budgets.

## 20. Testing strategy
- Unit tests (xUnit): Tool Gateway policies, routing, egress classification, date parsing, permissions, residency decisions.
- An LLM eval suite (EN/FR golden prompts with expected routes and tool calls, persona checks, injection resistance), run locally against the pinned models.
- Integration: automation against Notepad, Explorer and a test WinUI app; gateway contract tests with a stub upstream; extension tests on local test pages, including booby-trapped ones.
- Audio: recorded fixtures kept outside git, for wake-word false wakes and misses and for STT accuracy.
- Performance: latency and resource benchmarks; a 24-hour idle soak test.
- A manual acceptance script per phase. Nothing is "Tested" in STATUS.md until it has been verified on the target laptop.

## 21. Planned repository layout

```
src/
  Aldren.Core/                 background runtime host, orchestrator, supervisor
  Aldren.App/                  WinUI 3 UI (island, expanded window, settings, tray)
  Aldren.Contracts/            shared DTOs, IPC contracts, tool schemas
  Aldren.Brains/               Quick / Action / Research brain logic
  Aldren.Inference/            llama-server management, residency manager
  Aldren.Cloud/                Cloud Policy Layer, gateway supervisor, direct adapters
  Aldren.Research/             search adapters, page fetcher, citation checker
  Aldren.Voice/                audio, wake word, VAD, STT, TTS
  Aldren.Windows/              UI Automation, windows, files, apps
  Aldren.Perception/           capture, OCR, vision pipeline
  Aldren.Browser/              native-messaging host, Playwright channel
  Aldren.Memory/               SQLite store, embeddings, retrieval
  Aldren.Assistant/            reminders, tasks, notifications, briefing
  Aldren.Integrations.Google/  OAuth, Calendar, Drive
  Aldren.Presence/             home/away, modes
  Aldren.Focus/                focus sessions and enforcement
  Aldren.Security/             Tool Gateway, policies, permissions, audit, secrets
extensions/chrome/             ALDREN Companion extension (TypeScript, Manifest V3)
tools/wakeword-trainer/        Python, offline only
tools/bench/                   benchmark harness
tests/                         unit, integration and eval suites
docs/                          architecture, roadmap, status, decisions, benchmarks
spikes/                        Phase 0 throwaway prototypes
```

## 22. Defaults adopted at approval

| Decision | Default |
|---|---|
| Spoken language | English only |
| Cloud spending | Free tiers by default; optional paid Gemini key (cap ~€5–10/month) for grounded research with sources |
| Browser control | Companion extension in your main Chrome, plus a separate ALDREN automation profile |
| Microphone | Maono PD300X; never Bluetooth headset microphones unless you choose them |
| UI framework | WinUI 3; WPF if the Phase 0 spike fails |
| Data location | `E:\AldrenData` |
| Screenshots to the cloud | Never by default; per-request consent with a preview |
| Wake-word training | 50–100 recordings plus a 1–2 h GPU training run in Phase 0 |
| Cloud gateway | FreeLLMAPI, headless, supervised by ALDREN |
| Gateway catalog / Premium | Monthly signed snapshot on; no Premium |
| Allowed providers | Gemini (your key), Groq, Cerebras, Mistral, OpenRouter, GitHub Models |
| Quick Brain cloud fallback | Off until you enable it; then only for non-sensitive requests |
| Action Brain cloud vision | Ask every time, with a preview |
| Search step | Gateway search layer if Phase 0 confirms it works; otherwise direct Tavily, Brave or SearXNG |

## 23. Key references (checked 2026-10-31)
- FreeLLMAPI repository and docs: https://github.com/tashfeenahmed/freellmapi
- llama.cpp router mode: https://glukhov.org/llm-hosting/llama-cpp/llama-server-router-mode/
- Qwen3.5-4B GGUF (file sizes, vision projector): https://huggingface.co/SoAIHQ/Qwen3.5-4B-GGUF
- Chrome remote-debugging change in Chrome 136: https://developer.chrome.com/blog/remote-debugging-port
- Chrome consent-based debugging of a running browser: https://developer.chrome.com/blog/chrome-devtools-mcp-debug-your-browser-session
- Windows App SDK 2.0 release notes: https://learn.microsoft.com/en-au/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
- UFO², a hybrid UIA + vision Windows agent: https://arxiv.org/html/2504.14603v2
- Google OAuth refresh tokens in Testing vs Production: https://developers.google.com/health/setup
- Gemini pricing and Search grounding: https://ai.google.dev/gemini-api/docs/pricing · https://ai.google.dev/gemini-api/docs/google-search
- End of Picovoice's free tier (June 2026): https://community.home-assistant.io/t/fyi-picovoice-confirmed-free-tier-accesskeys-will-stop-working-after-june-30-2026/1012744
- Open ASR Leaderboard: https://arxiv.org/abs/2510.06961v2
- Windows 11 24H2 Wi-Fi and location change: https://kb.devicetrust.com/changes-to-for-wi-fi-access-and-location-windows-11-24h2
