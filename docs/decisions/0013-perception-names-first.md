# ADR-0013: Find controls by name first; vision chooses among a few candidates

- **Status:** Proposed (2026-11-01); awaiting your approval. Once accepted, it changes the perception ladder in ARCHITECTURE.md §11.
- **Refines:** the numbered-overlay level of the perception ladder (ARCHITECTURE.md §11) and the note on GUI-grounding models in §7.

## Context
Phase 0 (S5) measured the perception ladder on 30 windows you captured: Explorer 12, Chrome 10, Epic Games Launcher 3, and one each of Steam, VS Code, PowerShell, Discord and Claude ([results](../benchmarks/phase0.md#s5-screen-perception)).
- **UI Automation works where apps expose it:** 478 ms p90 overall, 325 ms in Chrome and 648 ms in Explorer, whose windows held a median 337 named controls. Discord, Steam and Epic exposed no named controls, and VS Code and Claude only 4.
- **Windows OCR read 67% of the UI labels drawn in the captures** (target 90%). Where it read a label, its position matched UI Automation's within a few pixels. The misses were labels read as nothing or as different text, and a 2× upscale only reached 68.5%.
- **The numbered overlay didn't work on real windows.** With boxes over up to 60 controls, Qwen3.5-4B picked the named control's box 24% of the time (target 80%), and pointing at raw coordinates found it 29% of the time, in 3.2 s p50 and 4.9 s p90. On images scaled to 1280 px wide it did worse (15% and 22%), so blurred number tags aren't the cause. On a synthetic window with 13 controls it managed 89% and 67%.

The ladder assumed the model could answer "#14" among all of a window's controls. At this model size it can't, and it is the slowest local step.

## Decision (proposed)
1. **Names first, in code.** A request that names a control ("click Save", "open the Downloads folder") is matched by deterministic code, not the model: first against UI Automation names, automation IDs and roles, then against OCR text where UI Automation has nothing. A single match goes straight to the Tool Gateway's usual checks, without calling the vision model.
2. **Vision chooses among a few candidates.** When several elements match, or the reference is visual or positional ("the blue one", "the icon next to the search bar"), the overlay numbers only a short list of candidates instead of every control. The first Phase 5 experiment measures how short the list must be and what accuracy it reaches; the working assumption is up to about eight.
3. **Scoped snapshots.** UI Automation reads the focused pane or the region in question rather than every row of a large window, to bring Explorer under 500 ms.
4. **Apps without accessibility data** (Electron and CEF apps such as Discord, Steam and Epic): Phase 5 first checks whether they expose their tree when asked for it. Otherwise vision works on the raw screenshot, with confirmation before acting, and pixel clicks stay a flagged last resort.
5. **Fallbacks to evaluate in Phase 5:** the 9B deep tier on the candidate list, a GUI-grounding model (swapped in like the 9B, since only one model stays resident) and a stronger OCR engine. Each is a download that needs your approval first.

## Alternatives considered
- **Keep the full numbered overlay:** 24% on your real windows.
- **Raw coordinates from the 4B:** 29%, and pixel clicks are a last resort by design.
- **Cloud vision by default:** screenshots are sensitive (per-request consent), and it adds latency and needs a connection. It stays an opt-in escalation.
- **Adopt a GUI-grounding model now:** it can't stay resident beside the 4B, so it would swap like the 9B. Worth measuring before adopting.

## Consequences
- Most "click X" requests never wait for the vision model. In apps that expose names, the slowest local step drops from 3–5 s to the UI Automation snapshot plus matching (0.3–0.65 s p90).
- Apps that expose nothing stay weak until Phase 5's evaluations, and KNOWN_LIMITATIONS says so.
- On acceptance, ARCHITECTURE.md §11 gains a name-matching level between UI Automation and OCR, and the numbered overlay covers a short candidate list only.
- Phase 5's benchmark includes the candidate-list experiment and sets its targets from S5's numbers.

## Revisit if
The candidate-list experiment stays well short of 80% with a handful of boxes, or a grounding model that fits the VRAM budget beats name matching in Phase 5.
