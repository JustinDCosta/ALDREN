# ADR-0003: Two main processes, named-pipe IPC, no Windows Service

- **Status:** Accepted (2026-10-31)

## Context
ALDREN must start at logon and keep listening and firing reminders even if its UI crashes. It also runs heavy native components (CUDA inference, the cloud gateway) that must not be able to take the assistant down.

## Decision
- **ALDREN.Core** (background runtime: orchestrator, brains, services, Tool Gateway, audio) starts at logon through a per-user startup entry, delayed 10–20 s.
- **ALDREN.App** (WinUI 3: island, expanded window, settings, tray) is started hidden by Core. Core restarts it on crash, and the App relaunches Core if Core dies.
- **Child processes:** llama-server, the optional FreeLLMAPI gateway and the Chrome native-messaging host (started by Chrome). Core places its own children in a Job Object so they never outlive it.
- **IPC:** StreamJsonRpc over a named pipe restricted to the current user (no TCP port).
- **Not a Windows Service:** services run in session 0, away from the interactive desktop and audio context an assistant needs.

## Alternatives considered
- **Single process:** simpler, but a UI crash would kill listening, reminders and running tasks.
- **Windows Service:** needs admin rights and runs in the wrong session.
- **gRPC or HTTP IPC:** heavier, and exposes a port.

## Consequences
- Typed shared contracts (Aldren.Contracts), with streaming events for tokens and state.
- Supervision and restart logic are part of Phase 1.

## Revisit if
The measured memory overhead of two processes becomes a problem.
