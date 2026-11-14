# Phase 7 plan: Browser and Google Drive

Status: approved 2026-11-08 ("move on with phase 7, 8 and 9, go through everything, download whatever you need"). Short record; acceptance afterwards.

## Downloads and system changes
| Item | Why |
|---|---|
| NuGet `Google.Apis.Auth`, `Google.Apis.Calendar.v3`, `Google.Apis.Drive.v3` (official Google .NET clients) | Sign-in (installed-app flow, loopback redirect, PKCE), Calendar (Phase 4, blocked until now), Drive |
| Registry, **only when you click "Connect Chrome/Edge" in Settings > Browser**: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.aldren.companion` (and Edge's equivalent) pointing at the host manifest next to ALDREN | Lets the ALDREN Companion extension start ALDREN's browser host. "Disconnect" removes it |
| Loading the extension: by you, in chrome://extensions (developer mode, "Load unpacked"); Chrome 156 ignores command-line extensions (S6) | |

Google sign-in stays **Blocked** until you create the Google Cloud client ([guide](../guides/google-oauth.md)) and paste its ID and secret into Settings > Google account (kept in Credential Manager). Deferred: the Playwright channel (the extension covers the exit criteria; Playwright adds ~200 MB of browsers and CDP attach needs your consent each time).

## What gets built
| Piece | Where | Notes |
|---|---|---|
| ALDREN Companion (MV3) | `extensions/chrome` | Tabs, page reading (passwords never read), clicks, filling (never password or card fields), downloads, focus blocking rules with their own expiry (Phase 8), an "ALDREN is acting" badge |
| Browser host | `src/Aldren.BrowserHost` | Started by Chrome over stdio; relays to Core on a current-user-only pipe; refuses malformed or oversized messages (from S6) |
| Browser tools | Core | Tabs, read page (untrusted), open URL, click, fill, download, back, switch/close tab; buttons that buy, pay, submit, send, delete or sign out are tier 3; filling forms asks unless you allowed forms on that site; executable downloads tier 3; sites can be set read-only |
| Google sign-in, Calendar, Drive | `src/Aldren.Integrations.Google` | Tokens in Credential Manager (never the library's plaintext store); Calendar agenda, create (read back), delete (asks); Drive search, download/export, upload (asks), rename and move (asks); calendar in the briefing |
