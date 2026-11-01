# ADR-0005: Custom wake word; sherpa-onnx with Parakeet and Kokoro

- **Status:** Accepted (2026-10-31). Speech recognition and voice activity detection confirmed by Phase 0 spike S3; the wake word is still being trained (S4, below). The text-to-speech and spoken-language parts are superseded by [ADR-0011](0011-supertonic-voice-english-only.md) (accepted 2026-11-01).

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

## Phase 0 result
Measured in S3 and S4 ([results](../benchmarks/phase0.md#s3-voice-pipeline)):
- **Speech recognition:** Parakeet-TDT int8 on the CPU transcribed your English commands at 5.4% word error rate in 137 ms (targets ≤ 10%, ≤ 400 ms), once 300 ms of audio from before the VAD fires is kept.
- **Wake word:** trained on 40,000 synthetic clips (Piper, 904 speakers), your 54 takes and real false alarms from your normal use. Model 4 trained on the CPU in 23 minutes, not on the GPU. It misses 3.7% of your held-out takes (target ≤ 5%) but woke about 16 times per hour of your normal use (target ≤ 0.5), down from about 217 for model 3.
- **The approach stays for now.** Next: retraining on model 4's own false alarms, more of your takes, and a transcript check biased toward the name. A plain transcript check can't be bolted on, because Parakeet hears your "ALDREN" as other words in a third of takes. The wake word has to reach its targets, or this decision is revisited, before Phase 3 relies on it; the hotkey works without it.
- **Runtime format:** the classifier is saved as `.npz` for now. The C# runtime needs it as ONNX (exporting needs the `onnx` package, which needs your approval) or as a small forward pass written in C#.

## Revisit if
False wakes or misses stay above target after tuning, or speech latency misses the voice budget.
