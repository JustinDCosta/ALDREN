# Status

Last updated: 2026-11-01 · Current phase: **Phase 1, foundation (plan awaiting your approval)**. Phase 0 results approved on 2026-11-01

Labels: **Planned** · **In progress** · **Implemented** (code written, not yet verified) · **Tested** (verified on the target laptop) · **Partially working** · **Blocked** (external requirement)

## Project

| Item | Status | Notes |
|---|---|---|
| Architecture v2 (including the FreeLLMAPI gateway) | Approved | 2026-10-31 |
| Documentation baseline | Implemented | Awaiting your review |
| Git repository | Implemented | Phase 0 work is on `main` (the `phase-0` branch was fast-forwarded into it on 2026-11-01) |
| Phase 0 plan | Approved | 2026-10-31, [plans/phase-0.md](plans/phase-0.md) |
| Phase 0 results | Approved | 2026-11-01, [summary](benchmarks/phase0.md#summary); ADR-0013 accepted. Open checks moved to the phases that need them (below) |
| Phase 1 plan | Implemented | Awaiting your approval: [plans/phase-1.md](plans/phase-1.md). Nothing is downloaded or changed before you approve it |

## Features by phase

| Feature | Phase | Status | Notes |
|---|---|---|---|
| Benchmark tool (`tools/bench`) | 0 | Tested | Kept for regression runs |
| S1 local model benchmarks | 0 | Tested | Qwen3.5-4B resident, Qwen3.5-9B deep tier via restart, Qwen3.5-0.8B CPU fallback; see [benchmarks/phase0.md](benchmarks/phase0.md) |
| S2 island overlay | 0 | Tested | WinUI 3 prototype passes all 8 checks (automated on both screens, plus your OBS, Snipping Tool and smoothness checks) |
| Recording tool (`aldren-record`, shared by S3 and S4) | 0 | Tested | Fixed after the first session came out time-squeezed (PortAudio's WASAPI path ran the PD300X at half speed); the second session (112 takes, 2026-11-01) is clean |
| S3 voice pipeline probe | 0 | In progress | On your voice: 5.4% English word error rate, 137 ms recognition; Supertonic 3 speaks in ~210 ms on the CPU, so a turn takes ~0.75 s plus the model's first sentence. Left, before Phase 3: the live microphone, unplug and Bluetooth checks with you |
| S4 wake word | 0 | In progress | Model 4 in the field: 68 false wakes in 4.2 h of your normal use (~16/h, down from ~217/h for model 3), none for the wake word; misses 3.7% of your held-out takes. Targets: ≤ 0.5 false wakes per hour, ≤ 5% misses. Training continues before Phase 3 |
| S5 screen perception probe | 0 | Tested | On your 30 captures: UI Automation 478 ms p90 (Explorer 648 ms), but Discord, Steam and Epic expose no controls; Windows OCR reads 67% of the drawn UI labels (target 90%); the 4B model finds a named control 24% of the time with numbered boxes (target 80%) and 29% by pointing. [ADR-0013](decisions/0013-perception-names-first.md) (accepted): find controls by name first, vision among a few candidates |
| S6 browser bridge probe | 0 | In progress | Through Chrome: ping 0.3 ms p90 (target 50 ms); read, click and fill work; injected instructions stay inert; passwords stay on the page. Left, before Phase 7: the CDP attach steps |
| S7 network identity probe | 0 | In progress | One fingerprint covers your Ethernet and Wi-Fi; Home in 1.6–1.7 s after a Wi-Fi reconnect, and the hotspot reads Away. Each reconnect briefly reads Away while Windows identifies the network, so ALDREN will debounce Away. Left, before Phase 4: the reboot checks |
| S8 FreeLLMAPI gateway evaluation | 0 | Tested | Go ([ADR-0012](decisions/0012-gateway-go-chains-only.md)): tool calls 38/38 valid when answered, schema 20/20, 63/63 answers from a provider in the requested chain (the bench now also checks the model), canary never stored, only Groq, Google and OpenRouter contacted. Chains only (a bare model name reached two resellers); overhead and failover move to Phase 6's entry conditions |
| Core and App processes, IPC, tray, startup, configuration, logging | 1 | Planned | |
| Tool Gateway, permissions, audit log | 1 | Planned | |
| Local inference and residency manager | 2 | Planned | Models chosen in S1; the llama-server client must retry dropped keep-alive connections |
| Orchestrator, fast path, router, persona | 2 | Planned | |
| App launcher and basic system tools | 2 | Planned | |
| Basic gaming mode (GPU unload) | 2 | Planned | |
| Audio service, wake word, VAD, STT, TTS | 3 | Planned | Before starting: S3's live microphone checks, and the wake word at its targets (model 4: ~16 false wakes/h) |
| Island live states, follow-ups, interruption v1 | 3 | Planned | |
| Memory and memory inspector | 4 | Planned | |
| Reminders, tasks, notifications | 4 | Planned | |
| Google sign-in and Calendar | 4 | Planned | Needs your Google Cloud OAuth client (guide provided in Phase 4) |
| Daily briefing | 4 | Planned | |
| Home/away detection, greeting, away mute | 4 | Planned | Before starting: S7's reboot checks. Away only after ~3 s without a match |
| Window management and layouts | 5 | Planned | |
| File management with undo | 5 | Planned | |
| Screen perception (UIA, OCR, vision) | 5 | Planned | Follows ADR-0013; first experiment: vision choosing among a few candidates |
| Clipboard intelligence | 5 | Planned | |
| FreeLLMAPI gateway integration and Cloud Policy Layer | 6 | Planned | Go after S8 (ADR-0012). Before starting: measure the gateway's overhead and failover; then the drift check, deadlines and 15 s provider timeouts |
| Research pipeline with citations | 6 | Planned | |
| Gemini-native grounded answers | 6 | Planned | Optional; needs your Gemini key |
| Chrome companion extension and Playwright channel | 7 | Planned | Before starting: S6's consent-based attach. Chrome 156 ignores `--load-extension`, so the extension is installed by hand |
| Google Drive | 7 | Planned | |
| Focus mode | 8 | Planned | |
| Webcam presence | 8b | Planned | Optional, opt-in only |
| Full gaming and streaming modes, automations, developer assistant, voice typing, echo cancellation | 9 | Planned | |
| Installer, updates, soak testing | 9 | Planned | |

## Waiting on you
- Review and approve (or change) the [Phase 1 plan](plans/phase-1.md), including its package list and system changes.
- S4: leave the model 4 field listener running (since 2026-11-01 16:16, in the terminal tab "wake-word field test (model 4)") during normal use; its false alarms feed the next retrain.
- Before Phase 3, S3: a short live test on each microphone, unplugging the PD300X mid-capture once, and the Bluetooth headset check.
- Before Phase 4, S7: after 2 reboots on each adapter, `S7.Network.exe snapshot` (skip if done: reboots don't show in the watch log).
- Before Phase 7, S6: the CDP part only. Close the test-profile Chrome window, then run `CdpAttach.exe profile`. Turn remote debugging on at `chrome://inspect/#remote-debugging`, run `CdpAttach.exe attach` and approve Chrome's prompt, then turn it off again.
