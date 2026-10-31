# ALDREN

A local-first, JARVIS-inspired personal AI assistant for Windows 11. You summon it by saying "ALDREN" (pronounced *al-drin*) or by typing. It talks naturally, operates your apps, windows, files and browser through a safety-checked automation layer, understands your screen, manages reminders and your Google calendar, researches online when asked, and helps you focus.

> **Status:** architecture approved on 2026-10-31. No code yet; Phase 0 (prototypes and benchmarks) is next. See [docs/STATUS.md](docs/STATUS.md).

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
| [docs/SECURITY.md](docs/SECURITY.md) | Threat model, permissions, risk tiers, hardening |
| [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md) | Honest limits of the hardware, models and services |
| [docs/decisions/](docs/decisions/README.md) | Architecture decision records: why each major choice was made |

Setup and testing instructions will be added in Phases 0 and 1, when there is something to set up and test.

## Technology (planned)

C#/.NET 10 · WinUI 3 (Windows App SDK 2.x) · llama.cpp (CUDA) · sherpa-onnx (voice activity detection, speech-to-text, text-to-speech) · custom wake-word model · FlaUI / UI Automation · Windows Graphics Capture + Windows OCR · Chrome extension + Playwright · SQLite (FTS5 + sqlite-vec) · optional FreeLLMAPI cloud gateway.
