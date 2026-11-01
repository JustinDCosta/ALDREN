# Status

Last updated: 2026-11-01 · Current phase: **Phase 0, prototypes and benchmarks (in progress)**

Labels: **Planned** · **In progress** · **Implemented** (code written, not yet verified) · **Tested** (verified on the target laptop) · **Partially working** · **Blocked** (external requirement)

## Project

| Item | Status | Notes |
|---|---|---|
| Architecture v2 (including the FreeLLMAPI gateway) | Approved | 2026-10-31 |
| Documentation baseline | Implemented | Awaiting your review |
| Git repository | Implemented | `main` pushed to GitHub; Phase 0 work on branch `phase-0` |
| Phase 0 plan | Approved | 2026-10-31, [plans/phase-0.md](plans/phase-0.md) |

## Features by phase

| Feature | Phase | Status | Notes |
|---|---|---|---|
| Benchmark tool (`tools/bench`) | 0 | Tested | Kept for regression runs |
| S1 local model benchmarks | 0 | Tested | Qwen3.5-4B resident, Qwen3.5-9B deep tier via restart, Qwen3.5-0.8B CPU fallback; see [benchmarks/phase0.md](benchmarks/phase0.md) |
| S2 island overlay | 0 | Tested | WinUI 3 prototype passes all 8 checks (automated on both screens, plus your OBS, Snipping Tool and smoothness checks) |
| Recording tool (`aldren-record`, shared by S3 and S4) | 0 | Tested | Fixed after the first session came out time-squeezed (PortAudio's WASAPI path ran the PD300X at half speed); the second session (112 takes, 2026-11-01) is clean |
| S3 voice pipeline probe | 0 | In progress | On your voice: 5.4% English word error rate, 137 ms recognition; Supertonic 3 speaks in ~210 ms on the CPU, so a turn takes ~0.75 s plus the model's first sentence. Left: the live microphone, unplug and Bluetooth checks with you |
| S4 wake word | 0 | In progress | Trainer, evaluation and field listener built; best model misses 7.4% of your held-out takes but fires ~37 times/h on other audio (targets ≤ 5% and ≤ 0.5/h). Field test next: it needs ~10 h of your normal use |
| S5 screen perception probe | 0 | In progress | Selftest on a synthetic window passes end to end: UI Automation 144 ms, OCR reads 9/9 labels, numbered boxes 8/9 at ~0.5 s per question. The measurement needs your captures |
| Spikes S6–S8 | 0 | Planned | |
| Core and App processes, IPC, tray, startup, configuration, logging | 1 | Planned | |
| Tool Gateway, permissions, audit log | 1 | Planned | |
| Local inference and residency manager | 2 | Planned | Models chosen in S1; the llama-server client must retry dropped keep-alive connections |
| Orchestrator, fast path, router, persona | 2 | Planned | |
| App launcher and basic system tools | 2 | Planned | |
| Basic gaming mode (GPU unload) | 2 | Planned | |
| Audio service, wake word, VAD, STT, TTS | 3 | Planned | The wake word needs your recordings |
| Island live states, follow-ups, interruption v1 | 3 | Planned | |
| Memory and memory inspector | 4 | Planned | |
| Reminders, tasks, notifications | 4 | Planned | |
| Google sign-in and Calendar | 4 | Planned | Needs your Google Cloud OAuth client (guide provided in Phase 4) |
| Daily briefing | 4 | Planned | |
| Home/away detection, greeting, away mute | 4 | Planned | |
| Window management and layouts | 5 | Planned | |
| File management with undo | 5 | Planned | |
| Screen perception (UIA, OCR, vision) | 5 | Planned | |
| Clipboard intelligence | 5 | Planned | |
| FreeLLMAPI gateway integration and Cloud Policy Layer | 6 | Planned | Go/no-go in Phase 0 (S8) |
| Research pipeline with citations | 6 | Planned | |
| Gemini-native grounded answers | 6 | Planned | Optional; needs your Gemini key |
| Chrome companion extension and Playwright channel | 7 | Planned | |
| Google Drive | 7 | Planned | |
| Focus mode | 8 | Planned | |
| Webcam presence | 8b | Planned | Optional, opt-in only |
| Full gaming and streaming modes, automations, developer assistant, voice typing, echo cancellation | 9 | Planned | |
| Installer, updates, soak testing | 9 | Planned | |

## Waiting on you
- S3: a short live test on each microphone, unplugging the PD300X mid-capture once, and the Bluetooth headset check.
- S4: the wake-word field test, `aldren-listen --threshold 0.9` running during ~10 h of normal use over a few days.
- S5: about 30 window captures (Chrome, VS Code, Explorer, Discord, Steam and an error dialog) with `S5.Perception.exe capture`; the captures stay in `E:\AldrenData\bench\s5`.
- Later in Phase 0: the Chrome toggles, provider keys and network switching listed in the plan.
