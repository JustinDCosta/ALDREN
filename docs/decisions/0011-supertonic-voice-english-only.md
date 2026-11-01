# ADR-0011: Supertonic 3 voice; English-only speech

- **Status:** Accepted (2026-11-01), as the voice for now. Supersedes the text-to-speech and spoken-language parts of [ADR-0005](0005-voice-stack.md).

## Context
Phase 0 (S3) measured the voice pipeline on this laptop. Speech recognition (Parakeet-TDT, CPU) meets its budget on your recorded commands: 5.4% English word error rate in 137 ms. Text-to-speech with Kokoro does not: 2.3–2.9 s to first audio with the int8 model and ~0.68 s with fp32 for "Sure, boss.", against a 0.3 s target. sherpa-onnx 2.0 also stopped shipping a phonemiser, so Kokoro's English skips words it doesn't know ("Spotify"), and the installed Kokoro v1.1 has no French voice. On 2026-11-01 you decided that ALDREN speaks English only and that response speed comes first.

## Decision
- **Text-to-speech:** Supertonic 3 (int8) via sherpa-onnx on the CPU, 4 threads, 3 flow-matching steps (2 if speed matters more than polish): ~210 ms to first audio for a short reply. It reads characters directly, so no phonemiser is needed. Supertonic generates a whole utterance at once, so ALDREN splits replies into sentences and speaks the first one as soon as it is ready. The voice is preset style 5, your pick of the ten.
- **Spoken language:** English only. French speech is not a goal; recognition still accepts what it can.
- **End of speech:** 400 ms of silence ends a turn; recognition starts speculatively at 250 ms and is discarded if you keep talking. 300 ms of audio from before the VAD fires is kept, so first sounds aren't clipped.
- **Instant acknowledgements:** short pre-synthesised phrases ("Sure, boss.") may play while the reply is generated.

## Alternatives considered
- **Kokoro v1.1 (int8 or fp32) on the CPU:** too slow, and without a phonemiser it skips unknown words.
- **Kokoro on the GPU:** would be faster again, but needs the CUDA runtime for ONNX Runtime (~1.5 GB) and competes with the resident model for VRAM; not needed at ~210 ms.
- **Pocket TTS:** streams, but took 460 ms to first audio here, and its ONNX export is licensed for non-commercial use only.
- **Kokoro v1.0 with sherpa-onnx 1.13.8 (espeak-ng built in):** only needed for French, which is no longer required.

## Consequences
- About 0.75 s from your last word to the first sound, plus the model's first sentence (~0.25 s); ~0.4 s when an acknowledgement covers the wait.
- Voices are limited to Supertonic's ten presets (no voice cloning).
- sherpa-onnx publishes Supertonic 3 in int8 only; an fp32 export can be tried if the voice quality at 3 steps disappoints.

## Revisit if
- **You want a more natural voice, which you already do.** On 2026-11-01 none of the ten presets felt natural to you; voice 5 is a stopgap. In the voice phase (Phase 3), evaluate more natural-sounding engines, including ones on the GPU, within the VRAM left beside the resident model, and keep first audio near 0.3 s.
- Another engine starts speaking in under ~200 ms with better quality.
