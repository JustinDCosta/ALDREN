# Phase 0 plan: prototypes and benchmarks

**Goal:** measure, on this laptop, every assumption the architecture depends on, and turn the results into confirmed decisions before Phase 1.

**Approach:** eight small prototypes (spikes) plus a benchmark tool that is kept for later regression runs. Everything is .NET 10 except the offline wake-word trainer (Python 3.12). Results go to `docs/benchmarks/phase0.md` and update the decision records.

**Stack:** .NET 10 (SDK 10.0.403) · WinUI 3 / Windows App SDK 2.6.4 · llama.cpp build b12023 (CUDA 13.4) · sherpa-onnx 2.0.1 · NAudio 3.3.0 · FlaUI.UIA3 5.0.0 · Microsoft.Playwright 1.64.1 · xUnit · Python 3.12 with uv 0.13.6, PyTorch 2.15 (CPU) and openWakeWord 0.6.0 · FreeLLMAPI v0.16.14 on Node 24.

**Spec:** [ARCHITECTURE.md](../ARCHITECTURE.md) and the Phase 0 section of [ROADMAP.md](../ROADMAP.md).

## Global constraints
- Windows 11 only. .NET 10, pinned by `global.json` to SDK 10.0.403 (latest-patch roll-forward). Python 3.12 only inside `tools/wakeword-trainer`, never in the runtime.
- Downloads, models, caches and data live under `E:\AldrenData`. C: only receives the .NET SDK.
- Every download is pinned (exact version or build) and its SHA-256 is recorded in `docs/benchmarks/phase0.md`.
- Local servers bind 127.0.0.1 only and require a random API key or a login.
- Recordings, screenshots and anything else personal stay under `E:\AldrenData`: never committed, never uploaded, never sent to any online service.
- No system change beyond the list in "Setup".
- Spike code in `spikes/` is throwaway. `tools/bench` and `tools/wakeword-trainer` are kept.
- GPU measurements run plugged in, on the High performance plan, with no game or OBS running. Every result records the power state, GPU power limit and baseline VRAM.

## Review focus
Failure modes that would quietly invalidate results. Each has a check in the owning task.
1. **Other apps using the GPU** (Chrome video, Discord, NVIDIA Broadcast, which is usually running) inflate VRAM and latency. The bench snapshots GPU memory before each run and marks the run invalid if baseline usage exceeds 1,200 MiB (Task 2).
2. **Battery or a lower power mode** throttles the GPU. The bench refuses GPU runs on battery unless `--allow-battery` is passed, and labels such results (Task 2).
3. **The microphone changing mid-recording** (a Bluetooth headset connecting, the Broadcast mic toggling). Recorders pin the device and stop with a clear message if it disappears or changes format (Tasks 5 and 6).
4. **Personal data reaching git** (recordings, screenshots, bench output containing screen text). All of it is written outside the repo, and `tools/check-repo.ps1` fails if audio, images, models, databases or files over 5 MB are staged. It runs before every commit (Task 1).
5. **Mixed monitors and fullscreen apps.** The island is tested on the internal 144 Hz panel and on the Dell 60 Hz monitor at different scaling, and over a borderless-fullscreen window (Task 4).

## Setup

| Item | Version | Source | Download | Location |
|---|---|---|---|---|
| .NET SDK | 10.0.403 | builds.dotnet.microsoft.com (official installer, SHA-512 checked) | 205 MB (~0.9 GB installed) | C:\Program Files\dotnet (one UAC prompt) |
| llama.cpp | b12023, CUDA 13.4 build + CUDA runtime | github.com/ggml-org/llama.cpp releases | 158 MB + 404 MB | E:\AldrenData\tools\llama.cpp |
| Qwen3.5-4B Q4_K_M + vision projector (F16) | | huggingface.co/unsloth/Qwen3.5-4B-GGUF | 2.55 GB + 0.63 GB | E:\AldrenData\models |
| Qwen3.5-2B Q4_K_M | | huggingface.co/unsloth/Qwen3.5-2B-GGUF | 1.19 GB | E:\AldrenData\models |
| Qwen3.5-9B Q3_K_M | | huggingface.co/unsloth/Qwen3.5-9B-GGUF | 4.35 GB | E:\AldrenData\models |
| Gemma 4 E2B (Google QAT Q4_0) + projector | | huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf | 3.12 GB + 0.92 GB | E:\AldrenData\models |
| Granite 4.2 3B Q4_K_M | | huggingface.co/ibm-granite/granite-4.2-3b-GGUF | 2.09 GB | E:\AldrenData\models |
| LFM2.5-1.2B Q4_K_M | | huggingface.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF | 0.68 GB | E:\AldrenData\models |
| Qwen3.5-0.8B Q4_K_M | | huggingface.co/unsloth/Qwen3.5-0.8B-GGUF | 0.50 GB | E:\AldrenData\models |
| Parakeet-TDT-0.6B-v3 int8 | | sherpa-onnx releases (asr-models) | 465 MB | E:\AldrenData\voice\models |
| Kokoro multi-lang v1.1 int8 | | sherpa-onnx releases (tts-models) | 140 MB | E:\AldrenData\voice\models |
| Silero VAD | | sherpa-onnx releases (asr-models) | 0.6 MB | E:\AldrenData\voice\models |
| Keyword-spotting baseline (zipformer, gigaspeech) | | sherpa-onnx releases (kws-models) | 17 MB | E:\AldrenData\voice\models |
| Piper LibriTTS-R medium (904 voices, for synthetic training clips) | | sherpa-onnx releases (tts-models) | 78 MB | E:\AldrenData\voice\models |
| openWakeWord negative features (ACAV100M, 2,000 h) | | huggingface.co/datasets/davidscripka/openwakeword_features | 16.09 GB | E:\AldrenData\voice\trainer-data |
| openWakeWord validation features | | same dataset | 0.17 GB | E:\AldrenData\voice\trainer-data |
| uv, Python 3.12 and trainer packages (PyTorch 2.15 CPU, onnxruntime, sherpa-onnx 2.0.1, openwakeword 0.6.0, numpy, scipy, soundfile, sounddevice) | uv 0.13.6 | github.com/astral-sh/uv, PyPI, download.pytorch.org | ~0.6 GB | E:\AldrenData\tools\uv, E:\AldrenData\venvs (cache: E:\AldrenData\cache\uv) |
| NuGet packages (Windows App SDK 2.6.4, sherpa-onnx 2.0.1, NAudio 3.3.0, FlaUI.UIA3 5.0.0, Playwright 1.64.1, xUnit) | | nuget.org | ~0.6 GB | E:\AldrenData\cache\nuget (set by the repo's nuget.config) |
| FreeLLMAPI | v0.16.14 | github.com/tashfeenahmed/freellmapi (shallow clone) + npm dependencies (Electron download skipped) | ~0.6 GB | E:\AldrenData\gateway\freellmapi-0.16.14 |

Total: ~34 GB downloaded, almost all on E:, plus ~1 GB on C: for the .NET SDK. The 16 GB negative-feature set is the largest item; without it the total is ~18 GB, but wake-word false-alarm rates are expected to be worse.

Only if results call for them: Gemma 4 E4B (5.72 GB), Qwen3.5-9B Q4_K_M (5.29 GB), Kokoro full precision (348 MB), the PyTorch CUDA build (~3 GB) if CPU training takes over 2 hours, and the llama.cpp CUDA 12.4 build (640 MB) if the 13.4 build misbehaves.

System changes (all reversible):
- Install the .NET 10 SDK machine-wide (one UAC prompt).
- Create `E:\AldrenData` and its subfolders.
- Register a Chrome native-messaging host under `HKCU\Software\Google\Chrome\NativeMessagingHosts` for the Task 9 test extension; it is removed at the end of Task 9.
- In Chrome, turn on Developer mode, load the unpacked test extension, and turn on remote debugging at `chrome://inspect/#remote-debugging` for one test (turned off again afterwards).
- No startup entries, services, scheduled tasks, firewall rules or system-wide environment variables. If Windows Firewall asks about `llama-server` or `node`, Cancel is fine: they only listen on 127.0.0.1.

## Manual steps
- Click Yes on the .NET installer's UAC prompt.
- ~20 minutes of recording (Task 5): "ALDREN" 60–100 times in different ways, 30 short commands (20 English, 10 French) and 30 seconds of room noise.
- ~30 screen captures with a hotkey while using apps normally (Task 8).
- Chrome steps for Task 9 (~5 minutes).
- Free API keys for 3–4 providers (Groq, Gemini via AI Studio, OpenRouter, plus Mistral or Cerebras), entered directly in the gateway dashboard (Task 11). Keys are never pasted into files, scripts or messages.
- Switch between Ethernet and Wi-Fi a few times, and once to another network such as a phone hotspot (Task 10).
- Leave the listen-only wake-word test running during ~10 hours of normal use over a few days (Task 7). It keeps only 2-second clips around each trigger.
- Close games and OBS during GPU benchmarks.

Phase 0 takes roughly 1–2 weeks of part-time work, mostly limited by the recordings and the 10-hour field test.

## File structure

```
global.json                     SDK pin
nuget.config                    package cache on E:
Directory.Build.props           shared C# settings
.editorconfig
ALDREN.sln
tools/check-repo.ps1            blocks personal and large files from commits
tools/bench/                    benchmark tool (kept)
  Environment/                  power, GPU and VRAM snapshots
  Llama/                        llama-server process, streaming probe, SSE parsing
  Stats/                        percentiles
  Eval/                         eval cases and scoring
  Gateway/                      FreeLLMAPI routing-header checks
  Commands/                     smoke, models, gateway
  eval/cases.jsonl, tools.json  100 EN/FR eval cases and tool schemas
tests/Aldren.Bench.Tests/       tests for the benchmark tool
tests/Aldren.Spikes.Tests/      tests for spike logic worth keeping
tools/wakeword-trainer/         Python 3.12: recorder, synthesis, training, evaluation, field test (kept)
spikes/S2.Island/               WinUI 3 overlay prototype (S2.IslandWpf only if needed)
spikes/S3.Voice/                voice latency probe
spikes/S5.Perception/           UI Automation, OCR and vision probe
spikes/S6.BrowserBridge/        native-messaging host, extension/, testpage/, CDP attach
spikes/S7.Network/              network identity probe
spikes/S8.Gateway/              hardened gateway launch script
docs/benchmarks/phase0.md       results and decisions
```

---

### Task 1: Workspace and tooling

**Files:** create `global.json`, `nuget.config`, `Directory.Build.props`, `.editorconfig`, `ALDREN.sln`, `tools/check-repo.ps1`, `tests/Aldren.Bench.Tests/Aldren.Bench.Tests.csproj`, `tests/Aldren.Bench.Tests/SmokeTests.cs`.

- [ ] Download the .NET SDK 10.0.403 installer to `E:\AldrenData\downloads`, verify its SHA-512 against the official release metadata, and run it. Verify: `dotnet --version` prints `10.0.403`.
- [ ] Create `E:\AldrenData\{downloads,tools,models,voice\models,voice\recordings,voice\field,voice\trainer-data,cache\nuget,cache\uv,cache\npm,venvs,bench,gateway,browser-profile}`.
- [ ] Add `global.json` (sdk 10.0.403, rollForward latestPatch), `nuget.config` (globalPackagesFolder `E:\AldrenData\cache\nuget`), `Directory.Build.props` (Nullable and ImplicitUsings enabled, LangVersion latest, TreatWarningsAsErrors for `tools/` and `tests/` only), `.editorconfig`, `ALDREN.sln`.
- [ ] Write `tools/check-repo.ps1`: exit 1 listing the offending files if any staged file matches `*.wav *.flac *.mp3 *.png *.jpg *.gguf *.onnx *.npy *.db` or exceeds 5 MB. Verify: stage a dummy `.wav` → exit code 1 naming the file; unstage it.
- [ ] Download llama.cpp b12023 (CUDA 13.4 build and runtime), check both against the release's published SHA-256 digests, unzip to `E:\AldrenData\tools\llama.cpp\b12023`. Verify: `llama-server --version` reports b12023 and `llama-server --list-devices` shows the RTX 3060 Laptop GPU.
- [ ] Download the seven models in the Setup table and check each against its Hugging Face SHA-256.
- [ ] Verify: `dotnet build ALDREN.sln` succeeds and `dotnet test` passes the smoke test.
- [ ] Run `tools/check-repo.ps1`, commit "Set up solution and Phase 0 tooling", push.

### Task 2: Benchmark runner core

**Files:** create `tools/bench/Aldren.Bench.csproj`, `tools/bench/Environment/EnvironmentSnapshot.cs`, `tools/bench/Llama/LlamaServer.cs`, `tools/bench/Llama/SseParser.cs`, `tools/bench/Llama/StreamingProbe.cs`, `tools/bench/Stats/Percentiles.cs`, `tools/bench/Commands/SmokeCommand.cs`; tests in `tests/Aldren.Bench.Tests/PercentilesTests.cs`, `SseParserTests.cs`, `EnvironmentSnapshotTests.cs`.

**Interfaces (produces):**
- `record EnvironmentSnapshot(bool OnAc, string PowerPlan, int GpuPowerLimitW, long BaselineVramMiB, IReadOnlyList<GpuProcess> GpuProcesses)`, `static Task<EnvironmentSnapshot> CaptureAsync(CancellationToken ct)`, `bool IsValidForGpuBench(bool allowBattery, out string reason)`.
- `record LlamaServerOptions(string ModelPath, string? ProjectorPath, int ContextSize, bool ProjectorOnCpu, string KvCacheType = "q8_0", IReadOnlyList<string>? ExtraArgs = null)`.
- `sealed class LlamaServer : IAsyncDisposable` with `static Task<LlamaServer> StartAsync(LlamaServerOptions options, CancellationToken ct)` (127.0.0.1, free random port, random API key, waits for `/health`), and `Uri BaseUri`, `string ApiKey`, `TimeSpan LoadTime`.
- `static IAsyncEnumerable<SseEvent> SseParser.ParseAsync(Stream stream, CancellationToken ct)`.
- `static Task<CompletionTiming> StreamingProbe.MeasureAsync(Uri baseUri, string apiKey, ChatRequest request, CancellationToken ct)` and `record CompletionTiming(double TtftMs, double TokensPerSecond, int OutputTokens, string Text)`.
- `static Percentiles Percentiles.Of(IReadOnlyList<double> samples)` and `record Percentiles(double P50, double P90, double P95, double Max)` (linear interpolation).

- [ ] Write tests: `Percentiles_of_1_to_100` (P50 50.5, P90 90.1, P95 95.05, Max 100); `Sse_parser_yields_chunks_then_done` (fixture with two `data:` chunks and `data: [DONE]` → two events, then completion); `Gpu_bench_invalid_on_battery`; `Gpu_bench_invalid_when_baseline_vram_above_1200_MiB` (1,300 → false, reason names the VRAM); `Gpu_bench_on_battery_allowed_with_flag`.
- [ ] Run `dotnet test` → the new tests fail (types missing).
- [ ] Implement. Power via `GetSystemPowerStatus`, plan via `powercfg /getactivescheme`, GPU via `nvidia-smi --query-gpu=power.limit,memory.used --format=csv,noheader,nounits`.
- [ ] Run `dotnet test` → pass.
- [ ] Verify: `dotnet run --project tools/bench -- smoke --model E:\AldrenData\models\Qwen3.5-0.8B-Q4_K_M.gguf` prints load time, TTFT and tokens/s, and afterwards no `llama-server` process remains.
- [ ] Commit "Add benchmark runner for local models", push.

### Task 3: S1 local model benchmarks and eval

**Files:** create `tools/bench/eval/cases.jsonl`, `tools/bench/eval/tools.json`, `tools/bench/Eval/EvalCase.cs`, `tools/bench/Eval/EvalScorer.cs`, `tools/bench/Commands/ModelsCommand.cs`; tests in `tests/Aldren.Bench.Tests/EvalScorerTests.cs`.

**Interfaces:**
- Consumes `LlamaServer`, `StreamingProbe`, `Percentiles`, `EnvironmentSnapshot` (Task 2).
- `record EvalCase(string Id, string Lang, string Kind, string Prompt, string? UntrustedContent, string? ExpectedRoute, string? ExpectedTool, JsonObject? ExpectedArgs)`, where Kind is `route`, `tool`, `persona` or `injection`.
- `record ModelResponse(string? Route, string? ToolName, JsonObject? ToolArgs, string Text)`, `record EvalResult(string CaseId, bool Passed, string? Reason)`, `static EvalResult EvalScorer.Score(EvalCase c, ModelResponse r)`.

Eval set (100 cases, ~70% English, ~30% French): 40 routing (fast path, quick, action, research), 30 tool calls against `tools.json` (`launch_app`, `set_volume`, `create_reminder`, `get_agenda`, `delegate_to_action`, `request_research`), 15 injection (untrusted content containing commands; pass = no tool call and no compliance), 15 persona (automatic checks: answers in the user's language, at most two sentences for small talk; plus a manual spot check of 10).

- [ ] Write tests: `Route_case_passes_on_exact_route`; `Tool_case_fails_on_wrong_argument` (`create_reminder` expects time `19:00`, got `07:00` → fail, reason `args.time`); `Injection_case_fails_when_any_tool_is_called`; `Malformed_tool_arguments_count_as_failure`.
- [ ] Run → fail. Implement `EvalScorer`. Run → pass.
- [ ] Write `cases.jsonl` and `tools.json`.
- [ ] Implement the `models` command. Per model: load time; warm TTFT with a fixed 1,500-token system prompt (10 runs, cached prefix); tokens/s over 256 output tokens; VRAM at 4K, 8K and 16K context; the eval set with tool schemas (`--jinja`, constrained tool calls); `llama-bench -p 512 -n 128`. For Qwen3.5-4B and Gemma 4 E2B, also answer a fixed non-personal test screenshot with the projector on GPU vs CPU (`--no-mmproj-offload`).
- [ ] Also measure: switching from the 4B to the 9B via router mode (`--models-dir`, `--models-max 1`) vs a process restart (stop → load → first token); and TTFT after a reload with a restored prompt slot (`--slot-save-path`) vs without.
- [ ] Run plugged in with games, OBS and NVIDIA Broadcast closed. Results go to `E:\AldrenData\bench\s1\<date>\` (`results.json`, `summary.md`).
- [ ] Check against the targets for the resident model: warm TTFT ≤ 400 ms; ≥ 30 tokens/s; VRAM ≤ 4.6 GB at 8K context with the projector; routing ≥ 90%; valid tool calls ≥ 95%; zero tool calls on injection cases; screenshot answer ≤ 4 s; 9B switch ≤ 4 s. Decision rule: the smallest model that meets the accuracy targets, then the fastest. Record the decision and the switching method chosen.
- [ ] Commit "Add model eval set and benchmark command", push.

### Task 4: S2 island overlay prototype

**Files:** create `spikes/S2.Island/` (WinUI 3, unpackaged, Windows App SDK 2.6.4 self-contained): `App.xaml`, `IslandWindow.xaml(.cs)`, `Native/WindowStyles.cs` (extended styles, display affinity, hit testing), `Diagnostics/FrameStats.cs` (`DwmGetCompositionTimingInfo`).

Checklist, each recorded with evidence:
1. Fully transparent outside the pill.
2. Never steals focus: while a fixed test sentence is typed into Notepad, the island appears and morphs 20 times; no keystrokes lost and focus stays in Notepad.
3. Click-through outside the pill; clicks on the pill work.
4. Stays above normal and borderless-fullscreen windows.
5. Invisible to OBS display capture, Snipping Tool and Windows Graphics Capture; the toggle makes it visible.
6. Smoothness: ≥ 95% of frames on time across 50 morphs, at 60 Hz (Dell) and 144 Hz (internal panel).
7. Correct placement and scaling on both monitors at 100% and 125% scaling.
8. Idle ≤ 120 MB RAM and ~0% CPU; appears within 100 ms of the hotkey.

- [ ] Build the window: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, transparent background, `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, `WM_NCHITTEST` returning `HTTRANSPARENT` outside the pill, Composition spring animation of size and corner radius, hotkey via `RegisterHotKey` (Ctrl+Alt+Space) cycling through the states.
- [ ] Run checklist items 1–8 and record the results in `E:\AldrenData\bench\s2\results.md`.
- [ ] If any of items 1–5 still fails after reasonable fixes, build `spikes/S2.IslandWpf` against the same checklist and compare.
- [ ] Commit "Add island overlay prototype", push.

### Task 5: Recording tool and session (shared by S3 and S4)

**Files:** create `tools/wakeword-trainer/pyproject.toml` (Python 3.12, uv), `uv.lock`, `src/aldren_wakeword/record.py`, `src/aldren_wakeword/prompts.py`, `tests/test_record.py`.

**Interfaces (produces):** CLI `aldren-record --device <name> --out E:\AldrenData\voice\recordings --session <yyyy-mm-dd>`, writing 16 kHz mono 16-bit WAV files plus `manifest.jsonl` lines `{"file", "kind": "wake|command|noise", "text", "take", "cue"}`.

- [ ] Unzip uv 0.13.6 to `E:\AldrenData\tools\uv` (SHA-256 checked); run `uv python install 3.12`. Every uv command runs with `UV_CACHE_DIR=E:\AldrenData\cache\uv`.
- [ ] Write tests: `test_manifest_line_written_per_take`; `test_clipped_take_is_rejected` (peak ≥ 0.99 → retake requested); `test_device_loss_aborts_with_exit_code_2` (simulated device error → exit code 2 and the message "Input device disappeared").
- [ ] Run `uv run pytest` → fail. Implement with `sounddevice` (device pinned by name and host API). Prompts: 60–100 "ALDREN" takes with cues (normal, quiet, loud, tired, fast, slow, 1 m away, 2–3 m away, with music playing), 30 commands (20 English, 10 French) from `prompts.py`, 30 s of room noise. Run → pass.
- [ ] Recording session (~20 minutes) on the Maono PD300X.
- [ ] Run `tools/check-repo.ps1`, commit "Add recording tool for voice tests", push.

### Task 6: S3 voice latency probe

**Files:** create `spikes/S3.Voice/` (console): `Audio/CaptureDevice.cs` (NAudio WASAPI, pinned device ID), `Pipeline/VoiceProbe.cs` (sherpa-onnx Silero VAD → Parakeet → Kokoro), `Program.cs`.

- [ ] Device-pinning check: disable the selected input mid-capture → the probe stops with "Input device disappeared" (no silent fallback to another device).
- [ ] Measure per device (Maono PD300X, laptop array, NVIDIA Broadcast mic): end-of-speech delay with a 400 ms VAD hangover; STT time for the final segment; TTS time to the first audio chunk; real-time factors; word error rate on the 30 recorded commands from Task 5. CPU only, adding GPU only if the CPU misses budget.
- [ ] Bluetooth check: open the Edifier headset microphone and record whether playback switches to hands-free quality.
- [ ] Targets: STT ≤ 400 ms (CPU) for 3–5 s utterances; TTS first audio ≤ 300 ms; endpoint + STT + TTS ≤ 1.1 s, leaving ~0.4 s of the 1.5 s turn budget for the model's first sentence; English command word error rate ≤ 10% (French reported). Results in `E:\AldrenData\bench\s3\`.
- [ ] Commit "Add voice latency probe", push.

### Task 7: S4 wake word v0

**Files:** create in `tools/wakeword-trainer/src/aldren_wakeword/`: `metrics.py`, `synth.py`, `features.py`, `train.py`, `evaluate.py`, `listen.py`; tests `tests/test_metrics.py`, `tests/test_synth.py`.

**Interfaces:**
- `metrics.false_reject_rate(scores: np.ndarray, threshold: float) -> float`
- `metrics.false_accepts_per_hour(scores: np.ndarray, threshold: float, hours: float, refractory_frames: int) -> float` (consecutive frames above threshold count as one event)
- `synth.wake_texts() -> list[str]`, `synth.near_miss_texts() -> list[str]`, `synth.generate(texts: list[str], out_dir: Path, voices: VoiceSet, count: int) -> list[Path]`
- `train.train(positive_dir: Path, negative_features: list[Path], out_onnx: Path, epochs: int) -> TrainReport`
- CLIs: `aldren-synth`, `aldren-train`, `aldren-eval`, `aldren-listen`.

- [ ] Write tests: `test_frr_counts_misses` (scores `[0.9, 0.2, 0.8, 0.1]`, threshold 0.5 → 0.5); `test_fa_per_hour_merges_consecutive_frames` (frames above threshold at 10, 11, 12 and 500, refractory 20, 1 hour → 2.0); `test_wake_texts_cover_respellings` (contains "Aldren", "Al-drin", "All-drin"); `test_near_misses_listed` (contains "Aldrin", "Alden", "all then", "Darren", "children").
- [ ] Run → fail. Implement `metrics.py` and the text lists. Run → pass.
- [ ] Install trainer dependencies (PyTorch 2.15 CPU, onnxruntime, sherpa-onnx 2.0.1, openwakeword 0.6.0 without extras, numpy, scipy, soundfile). Download the negative and validation features and the Piper LibriTTS-R voices.
- [ ] Synthesise ~20,000 positive clips (LibriTTS-R speakers + Kokoro voices, respellings, speed 0.8–1.2) and ~20,000 near-miss negatives. Augment with the recorded room noise, reverb and microphone EQ. Split the personal recordings 70/30 into training and held-out sets.
- [ ] Extract features with openWakeWord's melspectrogram and embedding ONNX models. The 16 GB negative-feature file is memory-mapped, never loaded whole (~7.7 GB of RAM is free).
- [ ] Train the classifier on the CPU (switch to CUDA PyTorch only if training exceeds 2 hours) and export `E:\AldrenData\voice\models\aldren.onnx`.
- [ ] Evaluate: false rejects on held-out recordings; false accepts per hour on the validation features; choose the threshold. Baseline: the sherpa-onnx keyword spotter on the same held-out set.
- [ ] Field test: `aldren-listen` runs on the Maono microphone during ≥ 10 hours of normal use over several days. It logs hours monitored, triggers and CPU use, and saves only 2-second clips around each trigger to `E:\AldrenData\voice\field`.
- [ ] Targets: held-out false-reject rate ≤ 5%; ≤ 0.5 false accepts per hour on both the validation set and the field test; detector ≤ 2% CPU. If missed: one retrain with the field false alarms added as negatives, then the voice-verifier option.
- [ ] Fallback if a training dependency fails on Windows: run the same trainer in the installed WSL Ubuntu distribution.
- [ ] Commit "Add wake word trainer and evaluation", push.

### Task 8: S5 perception probe

**Files:** create `spikes/S5.Perception/` (`net10.0-windows10.0.26100.0`): `Capture/CaptureSession.cs` (Windows Graphics Capture of the foreground window plus a UIA snapshot on Ctrl+Alt+S), `Uia/UiaSnapshot.cs` (FlaUI UIA3), `Ocr/OcrRunner.cs` (`Windows.Media.Ocr`), `Grounding/MarksRenderer.cs` (numbered boxes), `Grounding/GroundingProbe.cs` (llama-server with Qwen3.5-4B and its projector), `Program.cs`; tests in `tests/Aldren.Spikes.Tests/GroundingScoreTests.cs`.

**Interfaces:** `record UiElementInfo(int Id, string Name, string ControlType, Rect Bounds, bool IsEnabled)`, `static bool GroundingScore.PointInElement(Point p, UiElementInfo e)`, `static double GroundingScore.Accuracy(IReadOnlyList<(UiElementInfo Expected, Point? Predicted)> results)`.

- [ ] Write tests: `Point_on_edge_counts_as_inside`; `Null_prediction_counts_as_miss`; `Accuracy_of_three_hits_in_four_is_0_75`.
- [ ] Run → fail. Implement `GroundingScore`. Run → pass.
- [ ] Implement capture mode, then take ~30 captures across Chrome, VS Code, Explorer, Discord, Steam and an error dialog. Captures and UIA trees are stored in `E:\AldrenData\bench\s5`. Ground truth comes from the UIA bounds, so no manual labelling is needed.
- [ ] Measure: UIA snapshot time per app (p90 target ≤ 500 ms) and coverage (named interactive elements); OCR time and agreement with UIA names (target ≥ 90% on English UI text; French OCR availability noted); numbered-overlay selection accuracy (target ≥ 80%); raw-coordinate accuracy (reported); latency per question (target ≤ 4 s).
- [ ] Commit "Add perception probe", push.

### Task 9: S6 browser bridge probe

**Files:** create `spikes/S6.BrowserBridge/Host/` (C# console: native messaging over stdio), `extension/` (Manifest V3: `manifest.json`, `background.js`, `content.js`), `testpage/index.html` (buttons, a form, a result list, a paragraph containing injected instructions), `register-host.ps1`, `unregister-host.ps1`, `CdpAttach/` (Playwright console); tests in `tests/Aldren.Spikes.Tests/NativeMessagingTests.cs`.

**Interfaces:** messages `{"id": int, "cmd": "ping|read|click|fill", "args": {}}` → `{"id": int, "ok": bool, "result": {}, "error": string|null}`; `static byte[] NativeMessaging.Frame(string json)`; `static string? NativeMessaging.ReadFrame(Stream input)`.

- [ ] Write tests: `Frame_prefixes_little_endian_length`; `Read_frame_returns_null_at_end_of_stream`; `Read_frame_rejects_messages_over_1_MB`.
- [ ] Run → fail. Implement framing. Run → pass.
- [ ] Build the host and register it (`register-host.ps1`: HKCU key plus a host manifest allowing only the extension's ID). Turn on Developer mode, load the unpacked extension, and re-run the registration with its ID.
- [ ] Measure: ping round trip p90 (target ≤ 50 ms); reading the page (title, visible text, interactive elements with IDs); click and fill on the test page; the injected-instruction paragraph comes back as plain data and triggers nothing.
- [ ] CDP attach: turn on `chrome://inspect/#remote-debugging`; `CdpAttach` reads `DevToolsActivePort`, connects with Playwright, waits for Chrome's approval dialog, reads the active tab's title and disconnects. Turn remote debugging off again. Then launch a separate profile in `E:\AldrenData\browser-profile` through the installed Chrome.
- [ ] Clean up: run `unregister-host.ps1` and remove the unpacked extension.
- [ ] Commit "Add browser bridge probe", push.

### Task 10: S7 network identity probe

**Files:** create `spikes/S7.Network/` (console): `Nlm/NetworkSnapshot.cs` (Network List Manager: connected networks, IDs, categories, adapters; `GetBestRoute2` and `ResolveIpNetEntry2` for the gateway's MAC address; Wi-Fi name if permitted), `Presence/Fingerprint.cs`, `Presence/FingerprintMatcher.cs`; tests in `tests/Aldren.Spikes.Tests/FingerprintMatcherTests.cs`.

**Interfaces:** `record NetworkObservation(Guid? NetworkId, string? GatewayMac, string? Ssid, string AdapterDescription, bool IsVirtual)`; `enum PresenceState { Home, Away, Unknown }`; `static string Fingerprint.Of(Guid networkId, string gatewayMac)` (SHA-256 hex of `"{networkId:D}|{MAC upper-case, colon-separated}"`); `static PresenceState FingerprintMatcher.Evaluate(IReadOnlyList<NetworkObservation> observations, IReadOnlySet<string> homeFingerprints)`.

- [ ] Write tests: `Matching_fingerprint_is_home`; `Unknown_physical_network_is_away`; `No_physical_network_is_unknown`; `Virtual_adapter_alone_is_unknown` (only Tailscale up → Unknown); `Mac_formats_give_the_same_fingerprint` (`aa-bb-cc-dd-ee-ff` and `AA:BB:CC:DD:EE:FF`).
- [ ] Run → fail. Implement. Run → pass.
- [ ] Live: record the home fingerprints on Ethernet and on Wi-Fi; 10 reconnects and 2 reboots on each give a stable result; state known ≤ 2 s after the link comes up; a phone hotspot gives Away; Tailscale up or down changes nothing.
- [ ] Commit "Add network identity probe", push.

### Task 11: S8 FreeLLMAPI evaluation

**Files:** create `spikes/S8.Gateway/start-gateway.ps1`, `tools/bench/Gateway/RoutingHeaders.cs`, `tools/bench/Commands/GatewayCommand.cs`; tests in `tests/Aldren.Bench.Tests/RoutingHeadersTests.cs`.

**Interfaces:** `record RoutedVia(string Platform, string Model)`; `record FailedHop(string Platform, string Model, string Reason)`; `static RoutedVia? RoutingHeaders.ParseRoutedVia(string? header)` (percent-decoded); `static IReadOnlyList<FailedHop> RoutingHeaders.ParseTrail(string? header)`; `static bool RoutingHeaders.IsAllowed(RoutedVia via, IReadOnlySet<string> allowedPlatforms)`.

- [ ] Write tests: `Parses_routed_via`; `Decodes_percent_encoded_model_id`; `Parses_trail_with_two_hops` (`groq/llama-3.3-70b key1=rate_limited; google/gemini-2.5-flash key2=timeout` → two hops with reasons `rate_limited` and `timeout`); `Rejects_platform_outside_allowlist`.
- [ ] Run → fail. Implement. Run → pass.
- [ ] Shallow-clone tag v0.16.14 into `E:\AldrenData\gateway\freellmapi-0.16.14` and record the tag's commit SHA. Run `npm ci` with `ELECTRON_SKIP_BINARY_DOWNLOAD=1` and `--cache E:\AldrenData\cache\npm`, then `npm run build`.
- [ ] Write `start-gateway.ps1`. It generates a 32-byte `ENCRYPTION_KEY` once and stores it DPAPI-protected (user scope) in `E:\AldrenData\gateway\key.dpapi`; at launch it decrypts the key into the process environment only. It sets a random free `PORT`, `ALLOWED_HOSTS=localhost`, `FALLBACK_TIME_BUDGET_MS=8000`, `PROVIDER_STREAM_STALL_TIMEOUT_MS=15000`, `REQUEST_ANALYTICS_LOG_CLIENT=false`, `FREELLMAPI_UPDATE_CHECK=off` and `FREEAPI_DB_PATH=%LOCALAPPDATA%\Aldren\gateway\freeapi.db`, then runs `node server/dist/index.js`.
- [ ] Dashboard setup at `http://127.0.0.1:<port>`:
  - create the dashboard account and add 3–4 provider keys;
  - disable keyless, community and relay providers;
  - create the `aldren-fast` (Groq, Cerebras) and `aldren-vision` (Gemini Flash, Llama 4 Scout) chains, starting empty with auto-include off;
  - confirm MCP and Ollama emulation are off;
  - register the local llama-server as a temporary custom endpoint for the overhead test.
- [ ] In a terminal, run `dotnet run --project tools/bench -- gateway --url http://127.0.0.1:<port>`. It prompts for the gateway's unified key with input hidden. Checks:
  - (a) the listener is on 127.0.0.1 only;
  - (b) a request with `Host: evil.example` gets 403;
  - (c) memory after 10 idle minutes;
  - (d) per-request overhead: the local llama-server through the gateway vs directly, 20 requests each;
  - (e) failover after disabling the top key in `aldren-fast`: the request is served in-chain, `X-Routed-Via` is allowed and the trail records the failure;
  - (f) tool calls: 20 cases on each chain provider;
  - (g) `json_schema` output honoured;
  - (h) image requests: to `auto:aldren-vision` they are served by a vision model; to `auto:aldren-fast` they get 422;
  - (i) the `google_search` tool on a pinned Gemini model: record whether source links come back;
  - (j) whether the search layer returns structured results (only if a Tavily key is added);
  - (k) the node process's outbound connections during 30 idle minutes and a test burst;
  - (l) a unique canary string sent in a prompt must not appear in the gateway database or logs afterwards.
- [ ] Targets: (a) and (b) pass; memory ≤ 100 MB; median overhead ≤ 20 ms; failover stays in-chain 10/10; `X-Routed-Via` correct on every response; tool calls ≥ 95% valid; schema output ≥ 95%; image routing correct; only the catalog host and the configured providers contacted; canary absent. Go or no-go per ADR-0007.
- [ ] Remove the temporary custom endpoint and stop the gateway. Its data stays until Phase 6, or is deleted on request.
- [ ] Commit "Add gateway evaluation checks", push.

### Task 12: Results and decisions

**Files:** create `docs/benchmarks/phase0.md`; modify `docs/decisions/0002`, `0004`, `0005`, `0007`, `0009`, `0010`, `docs/STATUS.md`, `docs/KNOWN_LIMITATIONS.md`, and `docs/ARCHITECTURE.md` only if a decision changes (after approval).

- [ ] Write `phase0.md`: environment, versions and SHA-256 of every download, each spike's results against its targets, and the resulting decisions.
- [ ] Update the decision records (confirmed or revised; a changed decision gets a new record that supersedes the old one).
- [ ] Update STATUS (completed spikes, Phase 1 next) and replace estimates in KNOWN_LIMITATIONS with the measured values.
- [ ] Run `tools/check-repo.ps1`, commit "Add Phase 0 results", push.
- [ ] Review the results; the Phase 1 plan follows after approval.
