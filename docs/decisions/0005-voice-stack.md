# ADR-0005: Custom wake word; sherpa-onnx with Parakeet and Kokoro

- **Status:** Accepted (2026-10-31); to confirm in Phase 0 spikes S3 and S4. The text-to-speech and spoken-language parts are superseded by [ADR-0011](0011-supertonic-voice-english-only.md) (proposed 2026-11-01).

## Context
ALDREN listens continuously for "ALDREN" (*al-drin*), offline, with low CPU use and few false activations. It then transcribes and speaks with low latency, without taking GPU memory from the language model.

## Decision
- **Wake word:** an openWakeWord-style model trained for "ALDREN" on synthetic speech plus 50–100 of your recordings, with near-miss negatives and your own background audio. Optional voice-specific verifier and second-stage transcript check. Runs on the CPU via ONNX Runtime. Targets: ≤0.5 false wakes per hour and ≥95% detection at desk distance, measured rather than promised.
- **Voice activity detection:** Silero VAD.
- **Speech-to-text:** NVIDIA Parakeet-TDT-0.6B-v3 (int8) via sherpa-onnx, on the CPU by default; covers 25 European languages, including French.
- **Text-to-speech:** Kokoro-82M via sherpa-onnx, with British male voices (bm_george, bm_lewis, bm_daniel) and sentence-level streaming.
- **Training tools** live in tools/wakeword-trainer (Python, isolated environment), never in the runtime.

## Alternatives considered
- **Picovoice Porcupine:** excellent, but its free tier ended on 30 June 2026.
- **sherpa-onnx open-vocabulary keyword spotting:** needs no training; kept as the day-one baseline and for comparison.
- **Whisper large-v3-turbo, Qwen3-ASR-1.7B, Granite Speech:** more accurate but larger; candidates for dictation later.
- **Pocket TTS, Supertonic 3, Qwen3-TTS:** alternatives to benchmark (lower latency, better French, more expressive).

## Consequences
- You record the samples; training takes an estimated 30–90 min on the GPU.
- No real person's voice is cloned without rights.

## Revisit if
False wakes or misses stay above target after tuning, or speech latency misses the voice budget.
