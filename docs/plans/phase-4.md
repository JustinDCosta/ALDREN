# Phase 4 plan: Personal assistant

Status: approved 2026-11-08 ("continue… go in one go, we will debug later"). Short record; acceptance afterwards.

## Installs, downloads and system changes
None. SQLite (FTS5 is built into the bundled engine), the NVIDIA-free CPU code paths, Windows' Network List Manager and Core Audio are all already here. Notifications use the tray icon's balloons, which need no app registration in the registry.

Deferred because they would need a download, a registry change or an external service:
- Semantic memory search (sqlite-vec and the EmbeddingGemma model are downloads): keyword search (FTS5) plus recency for now.
- The OS-level backup notification for reminders while ALDREN isn't running (Task Scheduler or a registered toast are system changes): missed reminders are shown when ALDREN starts instead.
- Google sign-in and Calendar: **Blocked** on your Google Cloud project and the Google client packages; [the guide](../guides/google-oauth.md) explains what to create.
- Weather in the briefing: Open-Meteo would receive your location before the Phase 6 egress gate exists.
- Microsoft.Recognizers.Text, NodaTime and Ical.Net: replaced by a small deterministic English date parser (speech is English-only, ADR-0011) and .NET's own time zones; recurrence covers daily, weekdays and weekly.

## What gets built
| Piece | Where | Notes |
|---|---|---|
| Memory | `Aldren.Memory` | SQLite table plus an FTS5 index; "remember that…" saves at once; passwords, keys, card and ID numbers refused by detectors; recall, forget (asks first); profile facts and the best matches for each request go into the Quick Brain's context; never sent anywhere |
| Reminders | `Aldren.Assistant` | English date parser (in 10 minutes, at 7 pm, tomorrow at 9, Friday at 3 pm, 12 November at 10, every day/weekday/Monday at 8); one-time reminders stored as UTC instants, recurring ones as local wall-clock time; ambiguous times ("at 7") are asked about, never guessed; the result is read back; DST-safe |
| Scheduler | `Aldren.Assistant` + Core | Fires due reminders (tray notification + spoken when allowed), snooze, "while you were away" for reminders missed while ALDREN wasn't running; survives reboots |
| Tasks | `Aldren.Assistant` | Add, list, complete |
| Daily briefing | Core | Date, today's reminders, open tasks; spoken in at most a few sentences; also after the first home greeting of the day |
| Home/away | `Aldren.Presence` | Ported from S7: network fingerprint (Network List Manager ID + gateway MAC, hashed), physical adapters only; "this is my home network" saves it (asks first); Home at once, Away after a 20 s grace; "I'm home" overrides until the next network change |
| Greeting and away mute | Core | "Welcome back, boss." on Home after at least 4 h (sign-in or unlock), not during gaming or fullscreen; on Away, ALDREN saves the volume and mutes (not when the output is headphones), and restores it at home only if it did the muting and you haven't touched the volume since |
| Tools and fast path | Core | remember, recall, forget, create/list/cancel/snooze reminders, add/list/complete tasks, briefing, presence status, set home network; all through the Tool Gateway with their own permissions |
| Settings | Contracts, Core, App | Schema 3 (Home & away section); Memory, Reminders & tasks, and Home & away pages |

## Exit criteria (checked later, with you)
Reminders survive a reboot; date, DST and relative-date cases behave; the greeting plays only on the home network; memories can be inspected and deleted. Calendar criteria wait for the Google project.
