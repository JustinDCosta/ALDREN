# Phase 3 plan: Voice (MVP, milestone M1)

Status: approved 2026-11-08. Your decisions: push-to-talk hotkey first, the wake word opt-in (off by default) with a transcript check, training continuing alongside; one download approved. Speed over ceremony again: this is a short record, acceptance comes afterwards.

## Installs, downloads and system changes
| Item | Why | Pin |
|---|---|---|
| NuGet `Microsoft.ML.OnnxRuntime.Managed` 1.28.0 (~1 MB, official Microsoft) | Runs the wake word's openWakeWord feature models in C#. The managed-only package uses the `onnxruntime.dll` 1.28.2 that sherpa-onnx already ships, so there is one native runtime | Locked in `packages.lock.json` with its hash |

Already on the laptop, now referenced by the product (pinned as in Phase 0): `org.k2fsa.sherpa.onnx` 2.0.1, `NAudio` 3.3.0, and the models in `E:\AldrenData\voice\models` (Silero VAD, Parakeet-TDT 0.6B v3 int8, Supertonic 3 int8, openWakeWord melspectrogram + embedding, `aldren-wakeword-4.npz`). No system changes: the microphone is opened through normal WASAPI, so the Windows microphone indicator shows whenever ALDREN listens.

## What gets built
| Piece | Where | Notes |
|---|---|---|
| Audio input | `Aldren.Voice` | 16 kHz mono via WASAPI; device policy: PD300X, then the laptop array, then the default input, never a Bluetooth hands-free microphone; a lost device stops listening with a message instead of switching silently |
| VAD and recognition | `Aldren.Voice` | Silero VAD (400 ms end of speech, 300 ms pre-roll), Parakeet on the CPU; the transcript appears in the island when recognised (live partial captions need a streaming model: later) |
| Speech | `Aldren.Voice` | Supertonic 3 voice 5 on the CPU, one sentence at a time, first sentence first; other apps ducked to 30% while ALDREN speaks |
| Wake word (opt-in) | `Aldren.Voice` | Model 4 (`.npz` classifier over openWakeWord features via ONNX Runtime), threshold 0.95, 2 s cooldown, then the Parakeet transcript of the last 2 s must contain "Aldrin" or close (switchable). Off by default |
| Voice loop | `Aldren.Core/Voice` | Push-to-talk hotkey (Ctrl+Num *) or wake word → listen → recognise → orchestrator → speak → 8 s follow-up window without the wake word |
| Interruption v1 | `Aldren.Core/Voice` | The hotkey, the wake word or "stop" cancels speech; half-duplex: the microphone isn't processed while ALDREN speaks |
| Island live states | App | Listening, transcribing (with the transcript), thinking, speaking (with the reply), error |
| Quiet rules | `Aldren.Core/Voice` | No speech while a fullscreen app runs or gaming mode is on: the reply shows in the island only. Call detection (microphone in use by another app) is later |
| Settings | Contracts, Core, App | Settings schema 2 with a Voice section: wake word on/off, transcript check on/off, speak replies on/off, push-to-talk hotkey; the "Voice & wake word" page |

Deferred within Phase 3: the wake-word training wizard (the offline trainer in `tools/wakeword-trainer` stays the way to train), streaming captions, echo cancellation (Phase 9), the natural-voice evaluation (needs downloads; its own plan), S3's live microphone checks (part of this phase's acceptance instead).

## Exit criteria (checked later, with you)
"ALDREN… open VS Code" works end to end; median ≤ 1.5 s from end of speech to first audio with the model warm; wake-word false wakes and misses reported against the targets.
