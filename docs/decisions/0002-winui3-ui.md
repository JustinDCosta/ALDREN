# ADR-0002: WinUI 3 user interface, with WPF as fallback

- **Status:** Accepted (2026-10-31); confirmed by Phase 0 spike S2 (2026-11-01): the WinUI 3 prototype passed all eight checks, so the WPF fallback isn't needed

## Context
The UI must feel like a polished commercial Windows app. It centres on a Dynamic-Island-style overlay that appears instantly, never steals focus and animates fluidly, plus an expanded chat window and a settings application.

## Decision
- WinUI 3 on Windows App SDK 2.x (which supports fully transparent windows since 2.0), on .NET 10.
- The island uses Composition animations (springs run off the UI thread) in a pre-created hidden window: WS_EX_NOACTIVATE, topmost, click-through outside the pill, and excluded from screen capture by default.
- If the Phase 0 prototype fails any of these requirements, the UI switches to WPF (Fluent theme or WPF-UI), which the user already knows from ScreenDusk.

## Alternatives considered
- **WPF:** easier transparent overlays and very mature, but animations run on the UI thread and the look is less native to Windows 11. Kept as the fallback.
- **Tauri/React:** the best web animation ecosystem, but WebView2 memory use and a second language.
- **Electron, Avalonia, Qt:** heavier, a non-native look, or packaging overhead.

## Consequences
- Native Mica/Acrylic backdrops, Windows 11 controls and built-in accessibility.
- The XAML designer and hot reload need Visual Studio (optional); builds work from the command line.
- The island prototype is the first UI risk to retire.

## Phase 0 result
- S2 passed on the Dell (60 Hz, 100%) and the laptop panel (144 Hz, 125%): transparent outside the pill, no focus stealing, click-through, above borderless fullscreen windows, hidden from screen capture (OBS and Snipping Tool included), smooth morphs, correct placement, 35 MB of private memory, and the first frame 5.2 ms after the hotkey.
- What it took is recorded for Phase 3 ([results](../benchmarks/phase0.md#s2-island-overlay)). Two details matter beyond this record: click-through toggles `WS_EX_TRANSPARENT` from the cursor position, since `HTTRANSPARENT` doesn't pass clicks to other apps' windows, and the hotkey must be configurable with conflict detection, since Ctrl+Alt+Space was already taken on this PC.

## Revisit if
Spike S2 shows focus stealing, rendering artefacts, poor animation smoothness or unreliable transparency.
