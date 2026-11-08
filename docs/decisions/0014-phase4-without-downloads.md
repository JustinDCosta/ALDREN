# ADR-0014: Phase 4 built without new downloads

**Status:** Accepted (2026-11-08), under the Phase 4 approval ("go in one go"); open to review.

**Context.** ARCHITECTURE §14 planned Microsoft.Recognizers.Text (EN/FR dates), NodaTime, Ical.Net (RRULE), sqlite-vec with EmbeddingGemma (semantic memory) and an OS-level backup notification. Each is a download or a system change needing a separate approval, and you asked for Phase 4 in one pass.

**Decision.**
- Dates: a deterministic English parser in `Aldren.Assistant` (speech is English-only, ADR-0011) with .NET's `TimeZoneInfo` for DST; recurrence limited to daily, weekdays and weekly. Ambiguous times are asked about, never guessed; a time written with minutes ("9:00") reads as the 24-hour clock.
- Memory: SQLite FTS5 keyword search plus recency; no embeddings yet.
- Notifications: tray balloons (no app registration in the registry). Reminders missed while ALDREN wasn't running are shown at the next start instead of by an OS-level backup.
- Weather is left out of the briefing until the Phase 6 egress gate, since Open-Meteo would receive your location.

**Alternatives considered.** Downloading the planned libraries and model now (needs approval and adds ~0.3 GB for the embedding model); registering ALDREN for Windows app notifications (a registry change).

**Consequences.** No French dates or RRULE edge cases; keyword memory can miss paraphrases ("my sister" vs "my sibling"); a reminder can't fire while ALDREN is closed.

**Revisit if** keyword memory proves too weak (add sqlite-vec + EmbeddingGemma), you want reminders while ALDREN is closed (Task Scheduler or registered toasts), or recurrence needs grow (Ical.Net).
