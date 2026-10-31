# ADR-0004: llama.cpp, with one resident multimodal model shared by Quick and Action

- **Status:** Accepted (2026-10-31); model choices confirmed by Phase 0 spike S1

## Context
The RTX 3060 Laptop has 6 GB of VRAM, ~770 MiB of which the desktop already uses. Separate Quick and Action models can't both stay loaded, and swapping between them would cost 1–3 s per hand-off.

## Decision
- **Engine:** llama.cpp `llama-server`, pinned release, CUDA build. Loopback only, with a random API key. Full GPU offload, flash attention, 8-bit KV cache, 8–16K context, schema-constrained output for routing and tool calls, and the cached prompt prefix saved to disk.
- **Shared resident model:** Quick and Action both use **Qwen3.5-4B** (4-bit: ~2.8 GB plus a ~0.35–0.67 GB vision projector; ~3.8–4.6 GB of VRAM in total). The Action Brain is the same weights with vision, tools and a different prompt.
- **On demand:** **Qwen3.5-9B** loads for hard planning, swapping out the 4B.
- **CPU fallback:** a ~1B model (LFM2.5-1.2B or Qwen3.5-0.8B) for gaming and battery situations.
- **Embeddings:** EmbeddingGemma-300M on the CPU.
- **Residency manager** (VRAM monitored via NVML): keeps the model warm on AC power; unloads it on battery when idle, during fullscreen games or OBS, and under VRAM pressure; starts loading the moment the wake word fires.

## Alternatives considered
- **Ollama:** convenient, but less control (offload, KV cache, grammar) and it lags upstream features.
- **Separate Quick (1–2B) and Action (4–8B) models, both resident:** doesn't fit with useful context.
- **20–35B mixture-of-experts models with CPU offload:** need 13–20 GB of RAM plus VRAM.
- **Gemma 4 E4B as the resident model:** ~4.5 GB at 4-bit is too heavy alongside the context.

## Consequences
- No hand-off delay between Quick and Action, and vision is available without a swap.
- The quality ceiling of a 4B model applies; complex tasks escalate to the 9B or, with consent, to the cloud.

## Revisit if
Phase 0 shows the 4B fails the eval set, or another model fits better. The benchmarked alternatives are Gemma 4 E2B, Granite 4.2 3B and Qwen3.5-2B.
