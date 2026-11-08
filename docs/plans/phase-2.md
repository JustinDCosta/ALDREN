# Phase 2 plan: Quick Brain and orchestrator (text)

Status: approved 2026-11-08 ("let's get this going… in one go, we will do the testings later"). You asked for speed, so this is a short record of what the phase builds, not a task-by-task plan; acceptance checks come afterwards.

## Installs, downloads and system changes
None. Everything uses what is already on the laptop: llama.cpp b12023 and the Qwen3.5 4B, 9B and 0.8B models in `E:\AldrenData`, the NVIDIA driver's `nvml.dll`, and Windows APIs. No new NuGet packages.

Deferred: the embedding router (EmbeddingGemma-300M would be a download). The router uses explicit triggers plus the Quick Brain's delegation tools, which routed 95% of the Phase 0 eval correctly on their own.

## What gets built
| Piece | Where | Notes |
|---|---|---|
| llama-server manager | `Aldren.Inference` | Pinned b12023, 127.0.0.1, random port and API key, health check, Job Object (killed with Core), one retry of a dropped keep-alive connection, thinking off |
| Residency manager | `Aldren.Inference` | 4B on the GPU (8K context, q8_0 KV, projector on the GPU); loads on demand, preloads on AC power after start-up; unloads after 30 min idle, after 5 min on battery, and within ~2 s of a fullscreen game or OBS; free VRAM checked with NVML before loading; Qwen3.5-0.8B on the CPU when the GPU is off limits, stopped after 2 min idle |
| Orchestrator | `Aldren.Core/Orchestration` | Normalise → fast path → router → Quick Brain; conversation state (last turns), persona v1, cancellation and per-stage timeouts |
| Fast path | `Aldren.Core/Orchestration` | Grammar for open/close/switch to apps, volume, media, timers, time and date, calculator, gaming mode; runs through the Tool Gateway with no model |
| Router | `Aldren.Core/Orchestration` | Explicit triggers ("look up", "research", "online" → Research; "this", "on my screen" → Action); otherwise the Quick Brain, which answers or calls `delegate_to_action` / `request_research` (both reply that the feature arrives in Phase 5 / 6) |
| App launcher index | `Aldren.Windows` | Start menu and AppsFolder (desktop and Store apps) via the Shell, refreshed every 10 min, built-in aliases, fuzzy matching |
| System tools | `Aldren.Windows`, `Aldren.Core/Tools` | `launch_app`, `focus_app`, `close_app` (asks first), `set_volume`, `change_volume`, `mute`, `media_control`, `start_timer`, `get_date`, `calculate`; each with its own permission |
| Gaming mode | `Aldren.Inference` | Detected from Windows' fullscreen/D3D state and the OBS process, or switched by hand ("gaming mode on"); unloads the GPU model; media keys refused while it's on (no input into games) |
| Cloud fallback hook | `Aldren.Core/Orchestration` | Interface only; the implementation says no. 100% local |
| Timer notifications | Contracts + App | `IAldrenApp.NotifyAsync`; the App shows a tray notification |

## Exit criteria (checked later, with you)
Routing ≥ 90% on the eval set; first token < 500 ms with the model warm; VRAM freed < 5 s after a game goes fullscreen.
