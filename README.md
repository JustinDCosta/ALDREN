# ALDREN

A local-first, JARVIS-inspired personal AI assistant for Windows 11. You summon it by saying "ALDREN" (pronounced *al-drin*) or by typing. It talks naturally, operates your apps, windows, files and browser through a safety-checked automation layer, understands your screen, manages reminders and your Google calendar, researches online when asked, and helps you focus.

> **Status:** Phases 0–5 are done or in acceptance. Phase 6 (the cloud gateway under ALDREN's supervision, the Cloud Policy Layer and cited online research) is implemented; it needs your provider keys in the gateway dashboard. See [docs/STATUS.md](docs/STATUS.md).

## Design in one minute

- **One assistant, three brains.** A Quick Brain (fast local chat and commands), an Action Brain (screen understanding and PC control) and a Research Brain (online research). Quick and Action share one local multimodal model on the GPU; Research uses cloud models only when you ask.
- **Local-first.** Voice, chat, PC control, reminders and memory work offline. Nothing personal leaves the PC without passing ALDREN's egress checks and, for sensitive content, your explicit consent.
- **Safe by construction.** Models only *propose* actions; a deterministic Tool Gateway validates, permission-checks, confirms, executes, verifies and logs every one.
- **Built for this laptop.** RTX 3060 Laptop (6 GB), 16 GB RAM, Windows 11: one resident ~4B model, unloaded promptly for games and streaming.
- **Optional cloud gateway.** Cloud models are reached through a hardened, localhost-only FreeLLMAPI instance behind ALDREN's own privacy and consent rules.

## Documentation

| Document | What it covers |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | The approved design (the spec) |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Phases 0–9, deliverables and exit criteria |
| [docs/STATUS.md](docs/STATUS.md) | What is planned, implemented, tested, partially working or blocked |
| [docs/plans/phase-0.md](docs/plans/phase-0.md) | Phase 0 plan: prototypes and benchmarks |
| [docs/plans/phase-1.md](docs/plans/phase-1.md) | Phase 1 plan: foundation |
| [docs/plans/phase-2.md](docs/plans/phase-2.md) | Phase 2 plan: Quick Brain and orchestrator |
| [docs/plans/phase-3.md](docs/plans/phase-3.md) | Phase 3 plan: voice |
| [docs/plans/phase-4.md](docs/plans/phase-4.md) | Phase 4 plan: personal assistant |
| [docs/plans/phase-5.md](docs/plans/phase-5.md) | Phase 5 plan: Action Brain |
| [docs/plans/phase-6.md](docs/plans/phase-6.md) | Phase 6 plan: cloud gateway and Research Brain |
| [docs/guides/google-oauth.md](docs/guides/google-oauth.md) | Creating the Google Cloud project for Calendar and Drive |
| [docs/acceptance/phase-1.md](docs/acceptance/phase-1.md) | Phase 1 acceptance steps |
| [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md) | Pinned packages, licences and purposes |
| [docs/benchmarks/phase0.md](docs/benchmarks/phase0.md) | Phase 0 results: measurements on this laptop and the decisions they led to |
| [docs/SECURITY.md](docs/SECURITY.md) | Threat model, permissions, risk tiers, hardening |
| [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md) | Honest limits of the hardware, models and services |
| [docs/decisions/](docs/decisions/README.md) | Architecture decision records: why each major choice was made |

## Build, publish and run (Phase 1)

Needs the .NET SDK 10.0.403 (pinned in `global.json`). Packages restore into `E:\AldrenData\cache\nuget`, in locked mode against each project's `packages.lock.json`.

```
dotnet build ALDREN.sln
dotnet test tests\Aldren.Security.Tests
powershell -ExecutionPolicy Bypass -File tools\dev\publish-local.ps1
```

The publish script puts Core and the App side by side in `E:\AldrenData\dev\aldren` (it refuses while ALDREN runs from there). Start `Aldren.Core.exe`: there is no window, only the tray icon. Core starts and supervises the App. The hotkey opens the island text box; the tray menu opens the conversation window and Settings; tray > Quit stops both. Settings live in `%APPDATA%\Aldren`, the database and logs in `%LOCALAPPDATA%\Aldren` (set `ALDREN_HOME` to move both).

What to check after a build: [docs/acceptance/phase-1.md](docs/acceptance/phase-1.md). Idle CPU and memory: `dotnet run --project tools\bench -c Release -- idle --minutes 10`.

The Phase 0 prototypes are described, with how they were run, in the results.

## Technology (planned)

C#/.NET 10 · WinUI 3 (Windows App SDK 2.x) · llama.cpp (CUDA) · sherpa-onnx (voice activity detection, speech-to-text, text-to-speech) · custom wake-word model · FlaUI / UI Automation · Windows Graphics Capture + Windows OCR · Chrome extension + Playwright · SQLite (FTS5 + sqlite-vec) · optional FreeLLMAPI cloud gateway.
