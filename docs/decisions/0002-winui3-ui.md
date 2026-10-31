# ADR-0002: WinUI 3 user interface, with WPF as fallback

- **Status:** Accepted (2026-10-31); to confirm in Phase 0 spike S2

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

## Revisit if
Spike S2 shows focus stealing, rendering artefacts, poor animation smoothness or unreliable transparency.
