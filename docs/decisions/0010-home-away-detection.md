# ADR-0010: Home detection by network identity, not Wi-Fi name

- **Status:** Accepted (2026-10-31); to confirm in Phase 0 spike S7

## Context
"Welcome back, boss." and away mute both depend on knowing that you are home.
- You often use Ethernet, so there may be no Wi-Fi name at all.
- Wi-Fi names (SSIDs) are easy to spoof.
- Since Windows 11 24H2, reading them requires the "Let desktop apps access your location" permission.

## Decision
- **Signals:**
  - the active network profile from Windows' Network List Manager;
  - the default gateway's hardware address (your router's identity);
  - the Wi-Fi name or BSSID, when available and permitted.

  Virtual adapters (VPN, Tailscale, Hyper-V) are ignored.
- **States:** Home = a trusted fingerprint (profile + router identity, stored hashed) on any physical adapter; an unknown network = Away; no network yet = Unknown.
- **When:** checks run at startup, on network change, on resume and on unlock, with a 20 s grace period.
- **Greeting:** only when Home is confirmed, and not during calls, fullscreen games or meetings.
- **Away mute:**
  - applies on Away, and on Unknown after the grace period (configurable);
  - is skipped when the output is headphones;
  - the volume is restored only if ALDREN did the muting and you haven't touched it since.

## Alternatives considered
- **Wi-Fi name only:** fails on Ethernet, spoofable, and depends on a permission.
- **Public IP or geolocation:** a privacy cost, and unreliable.

## Consequences
- Works on Ethernet and Wi-Fi without the location permission.
- Spoofing a router's hardware address is possible, but the worst outcome is a greeting.

## Revisit if
S7 shows the router's identity isn't stable on your network.
