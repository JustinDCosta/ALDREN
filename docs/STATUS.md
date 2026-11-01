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
| S4 wake word | 0 | In progress | Field test: in 3.8 h of your normal use, model 3 woke 704 times (183/h), never for the wake word (target ≤ 0.5/h). Model 4, retrained on those false alarms, misses 3.7% of your takes and fires on 6 of 180 held-out field false alarms (model 3: 58), but still ~12/h on other audio. Next: a field run on model 4 |
| S5 screen perception probe | 0 | In progress | On your 30 captures: UI Automation 478 ms p90 (Explorer 648 ms), but Discord, Steam and Epic expose no controls; Windows OCR reads 67% of the drawn UI labels (target 90%); the 4B model finds a named control 24% of the time with numbered boxes (target 80%) and 29% by pointing. Phase 5 resolves controls by name first and narrows what vision has to choose from |
| S6 browser bridge probe | 0 | In progress | Through Chrome: ping 0.3 ms p90 (target 50 ms); read, click and fill work; injected instructions stay inert; passwords stay on the page. Left: the CDP attach steps |
| S7 network identity probe | 0 | In progress | One fingerprint covers your Ethernet and Wi-Fi; Home in 1.6–1.7 s after a Wi-Fi reconnect, and the hotspot reads Away. Each reconnect briefly reads Away while Windows identifies the network, so ALDREN will debounce Away. Left: the reboot checks |
| S8 FreeLLMAPI gateway evaluation | 0 | In progress | v0.16.14 built outside the repo and launched hardened: 127.0.0.1 only, foreign Host refused, encryption key DPAPI-protected. The go/no-go checks (failover, tool calls, images, canary...) need your provider keys and dashboard setup |
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
- S4: leave the model 4 field listener running (since 2026-11-01 16:16, in the terminal tab "wake-word field test (model 4)") for a few hours of normal use.
- S6: the CDP part only. Close the test-profile Chrome window, then run `CdpAttach.exe profile`. Turn remote debugging on at `chrome://inspect/#remote-debugging`, run `CdpAttach.exe attach` and approve Chrome's prompt, then turn it off again.
- S7: after 2 reboots on each adapter, `S7.Network.exe snapshot` (skip if done: reboots don't show in the watch log).
- S8, about 30 minutes: start the gateway (`spikes\S8.Gateway\start-gateway.ps1`), set it up in its dashboard (account, 3–4 provider keys typed there only, the two `aldren-*` chains), then run `dotnet run --project tools\bench -c Release -- gateway` (it finds the running gateway itself) and follow its prompts; `stop-gateway.ps1` at the end.
