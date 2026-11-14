# Phase 9 plan: Advanced features and polish

Status: approved 2026-11-08 (same message).

| Item | Download | Notes |
|---|---|---|
| Gaming and streaming modes | none | OBS running = streaming; notifications deferred while gaming or streaming (urgent ones excepted) and shown without personal text while streaming; "silent mode" |
| Routines | none | "create a routine called start my day: open Outlook, apply the coding layout, brief me"; run by its phrase; every step still goes through the Tool Gateway |
| Developer assistant | none | Read-only git status, log and diff summary for your repositories (fixed git commands, no shell); VS Code's open folder from its window title |
| Voice typing | none | "start dictation": what you say is typed into the focused field until "stop dictation"; never into password fields or games |
| Personality modes | none | Friendly (default), professional, sarcastic, serious, concise, detailed; "switch to concise mode" |
| Installer and updates | `vpk` (Velopack CLI) as a local dotnet tool in `E:\AldrenData\tools\vpk`, NuGet `Velopack` | A per-user installer and update packages built into `E:\AldrenData\releases`; you run Setup.exe yourself; code signing optional (needs a certificate) |
| Soak test | none | `aldren-bench idle --minutes 1440` for the 24-hour run |

Deferred: echo cancellation for interrupting over speakers (needs testing with your speakers), MCP interoperability and the deeper gateway integration (optional).
