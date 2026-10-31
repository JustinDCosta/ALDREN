# ADR-0009: Chrome extension first, Playwright on demand, UI Automation fallback

- **Status:** Accepted (2026-10-31); to confirm in Phase 0 spike S6

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

## Revisit if
Chrome changes its extension or debugging policies, or S6 shows consent-based attach is impractical.
