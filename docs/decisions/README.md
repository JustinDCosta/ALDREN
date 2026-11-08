# Architecture decision records

Each record explains one major decision: its context, the decision itself, the alternatives, the consequences and when to revisit it. Records are never edited to reverse a decision; a new record supersedes the old one.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-windows-dotnet-stack.md) | Windows-only, C#/.NET 10; Python only for offline tools | Accepted |
| [0002](0002-winui3-ui.md) | WinUI 3 (Windows App SDK 2.x) UI, with WPF as fallback | Accepted; confirmed by Phase 0 (S2), no fallback needed |
| [0003](0003-process-model.md) | Two main processes, named-pipe IPC, no Windows Service | Accepted |
| [0004](0004-local-inference.md) | llama.cpp; one resident ~4B multimodal model shared by Quick and Action | Accepted; confirmed by Phase 0 (S1): Qwen3.5-4B resident, 9B by restart, 0.8B CPU fallback |
| [0005](0005-voice-stack.md) | Custom wake word; sherpa-onnx with Parakeet and Kokoro | Accepted; speech recognition confirmed by Phase 0 (S3); text-to-speech and spoken language superseded by 0011; wake word still in training (S4: ~16 false wakes/h, target 0.5) |
| [0006](0006-tool-gateway-security.md) | Tool Gateway with risk tiers and untrusted-content quarantine | Accepted |
| [0007](0007-cloud-gateway-freellmapi.md) | FreeLLMAPI as an optional hardened cloud gateway, with an ALDREN Cloud Policy Layer | Accepted; S8 go (2026-11-01) with the conditions in 0012 |
| [0008](0008-research-pipeline.md) | ALDREN-owned research pipeline with verified citations | Accepted |
| [0009](0009-browser-automation.md) | Chrome extension first, Playwright on demand, UI Automation fallback | Accepted; extension and separate profile confirmed by Phase 0 (S6); consent-based attach to check before Phase 7 |
| [0010](0010-home-away-detection.md) | Home detection by network identity, not Wi-Fi name | Accepted; confirmed by Phase 0 (S7), Away after ~3 s; reboot checks before Phase 4 |
| [0011](0011-supertonic-voice-english-only.md) | Supertonic 3 voice on the CPU; English-only speech | Accepted (2026-11-01); a more natural voice to evaluate in Phase 3 |
| [0012](0012-gateway-go-chains-only.md) | Keep FreeLLMAPI after S8; curated chains only, ALDREN's providers only, short deadlines | Accepted (2026-11-01); overhead and failover measured before Phase 6 |
| [0013](0013-perception-names-first.md) | Find controls by name first; vision chooses among a few candidates | Accepted (2026-11-01) |
| [0014](0014-phase4-without-downloads.md) | Phase 4 without new downloads: own English date parser, keyword memory, tray notifications | Accepted (2026-11-08); open to review |

Template for new records: **Status** (with date) · **Context** · **Decision** · **Alternatives considered** · **Consequences** · **Revisit if**.
