# ADR-0009: Chrome extension first, Playwright on demand, UI Automation fallback

- **Status:** Accepted (2026-10-31); the extension channel and the separate automation profile confirmed by Phase 0 spike S6 (2026-11-01); the consent-based attach is still to check, before Phase 7

## Context
ALDREN must read and operate pages in your everyday Chrome, with your logins.
- Since Chrome 136, the remote-debugging switches are ignored on the default profile.
- An always-open debugging port would be a security hole.
- Chrome 144+ offers consent-based remote debugging of a running browser, with an approval dialog per session.

## Decision
- **Channel A (default):** an ALDREN Companion extension (Manifest V3) for Chrome and Edge, connected to ALDREN through a native-messaging host. It provides tab context, page reading, clicks and form filling, tab management, downloads and focus-mode website blocking.
- **Channel B (on demand):** Playwright for .NET over CDP, either attached to your running Chrome through Chrome's consent-based remote debugging, or in a separate ALDREN automation profile that never touches your logged-in sessions.
- **Channel C (fallback):** UI Automation for other browsers (URL, title, simple controls).
- **Safety:**
  - page content is untrusted;
  - per-site permission levels;
  - confirmations before submitting, buying, paying, changing accounts, sending messages or downloading executables;
  - ALDREN never types passwords or card numbers;
  - a visible "ALDREN is acting" indicator.

## Alternatives considered
- **CDP on the default profile:** blocked by Chrome by design.
- **UI Automation only:** slow and brittle for web content.

## Consequences
- An extension to build and keep loaded (unpacked). Locking it against being disabled may require publishing it to the Chrome Web Store; this will be evaluated with focus mode in Phase 8.

## Phase 0 result
Measured in S6 ([results](../benchmarks/phase0.md#s6-browser-bridge)):
- **Channel A:** through Chrome, the extension and its native-messaging host answered a ping in 0.3 ms p90 (target 50 ms), and read, click and fill took 1–1.5 ms. Password values never left the page, injected instructions on the test page triggered nothing, and the host refused anything but its four commands.
- **Channel B, separate profile:** Playwright drove the installed Chrome 156 on its own profile (454 ms to launch headless, a click in 114 ms), with no browser downloaded.
- **Channel B, attach to your Chrome:** not checked yet; it needs your steps.
- **Installing the extension:** Chrome 156 ignored `--load-extension`, so the extension is loaded by hand (Developer mode) or published. Its clicks are synthetic (`isTrusted` is false) and some sites ignore them; Playwright's input is real.

## Revisit if
Chrome changes its extension or debugging policies, or S6 shows consent-based attach is impractical.
