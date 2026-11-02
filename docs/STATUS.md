# Status

Last updated: 2026-11-02 · Current phase: **Phase 1, foundation (Tasks 1–12 done, starting Task 13)**. Phase 0 results approved on 2026-11-01

Labels: **Planned** · **In progress** · **Implemented** (code written, not yet verified) · **Tested** (verified on the target laptop) · **Partially working** · **Blocked** (external requirement)

## Project

| Item | Status | Notes |
|---|---|---|
| Architecture v2 (including the FreeLLMAPI gateway) | Approved | 2026-10-31 |
| Documentation baseline | Implemented | Awaiting your review |
| Git repository | Implemented | Phase 0 work is on `main` (the `phase-0` branch was fast-forwarded into it on 2026-11-01) |
| Phase 0 plan | Approved | 2026-10-31, [plans/phase-0.md](plans/phase-0.md) |
| Phase 0 results | Approved | 2026-11-01, [summary](benchmarks/phase0.md#summary); ADR-0013 accepted. Open checks moved to the phases that need them (below) |
| Phase 1 plan | Approved | 2026-11-02, [plans/phase-1.md](plans/phase-1.md); tests only for the Tool Gateway (your decision) |

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
| Core and App processes, IPC, tray, startup, configuration, logging | 1 | In progress | Task 1: solution scaffolding builds with 0 warnings, locked restore, `tools/dev/publish-local.ps1` publishes to `E:\AldrenData\dev\aldren`; packages in [DEPENDENCIES.md](DEPENDENCIES.md). Task 9: versioned `settings.json` (validated, atomic writes, migrations, corrupt file set aside, newer file left untouched), checked by hand against the six planned cases. Task 10: start-with-Windows toggle (HKCU Run value `"<path>" --startup`, respects being switched off in Task Manager); checked by hand on a fake registry against the five planned cases, the real registry not touched yet. Task 11: Core process (single instance, host, App supervisor with 15 s logon delay and backoff, pipe server, confirmation broker, placeholder commands with `get_time` and the confirmation test, audit retention, logoff handling); supervisor, job, broker and placeholder checked by hand, and the published Core ran on this laptop: backoff 1, 2, 4, 8, 16 s with no App, and a second Core exits at once. Task 12: App shell (single instance, hidden start, Core client with watchdog, tray icon and menu, Settings with every section, conversation window, Quit); on this laptop Core starts it with no window, a killed App returns in 1.6 s, a killed Core is relaunched by the App in 1.1 s, and you confirmed the tray, Settings, permission change in Activity history and Quit. Confirmations use a temporary dialog until the island (Task 13). Task 2: shared building blocks in `Aldren.Common` (paths, restart backoff, single instance, redacted logging, hotkey parsing and fallback, island placement), checked by hand against the plan's expected values. Task 3: IPC contracts and the current-user-only pipe; a manual check on a real pipe passed the five planned cases (calls both ways, cancellation, disconnect, second client refused) |
| Tool Gateway, permissions, audit log | 1 | In progress | Task 4: permission catalog and store (atomic writes, corrupt file set aside, newer file left untouched), checked by hand against the six planned cases. Task 5: risk tiers, typed tools, strict argument binder with exported schema, tool registry; checked by hand against the nine planned cases. Task 6: SQLite audit log (WAL, redacted and shortened arguments, UTC timestamps, pruning) and the current-user-only data folder ACL; checked by hand against the five planned cases. Recording permission changes is wired in Core (Task 11). Task 7: Tool Gateway (tier and taint policies, 60 s confirmation, timeouts, post-conditions, fails closed without the audit log, 8 KB result cap); 43 unit tests pass. Task 8: Credential Manager secret store (`Aldren/<name>`, local machine only, 2,560-byte cap); name and size checks plus one real round trip checked by hand, no `Aldren/` credential left behind |
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
- Nothing for Phase 1 until its acceptance steps.
- S4: leave the model 4 field listener running (since 2026-11-01 16:16, in the terminal tab "wake-word field test (model 4)") during normal use; its false alarms feed the next retrain.
- Before Phase 3, S3: a short live test on each microphone, unplugging the PD300X mid-capture once, and the Bluetooth headset check.
- Before Phase 4, S7: after 2 reboots on each adapter, `S7.Network.exe snapshot` (skip if done: reboots don't show in the watch log).
- Before Phase 7, S6: the CDP part only. Close the test-profile Chrome window, then run `CdpAttach.exe profile`. Turn remote debugging on at `chrome://inspect/#remote-debugging`, run `CdpAttach.exe attach` and approve Chrome's prompt, then turn it off again.
