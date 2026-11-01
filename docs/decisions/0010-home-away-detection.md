# ADR-0010: Home detection by network identity, not Wi-Fi name

- **Status:** Accepted (2026-10-31); confirmed by Phase 0 spike S7 (2026-11-01), with the refinements below; the reboot checks are still to do, before Phase 4

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

## Phase 0 result
Measured in S7 ([results](../benchmarks/phase0.md#s7-network-identity)):
- **The router's identity is stable.** One fingerprint (network ID and gateway MAC, hashed) covers your Ethernet and your Wi-Fi, which reach the same router. Virtual adapters are recognised by Windows' hardware-interface flag rather than by name.
- **Timing:** Home 1.6–1.7 s after a Wi-Fi reconnect; a phone hotspot read Away 1.9 s after connecting.
- **Refinements:** while Windows identifies a network that just connected, the probe read Away for about 1.5 s, so ALDREN announces Home as soon as a fingerprint matches but Away only after ~3 s without a match. The gateway's MAC comes from the ARP cache or a bounded wait: on a dropped link, waiting for an ARP answer stalled the check for 3–4 s.

## Revisit if
S7 shows the router's identity isn't stable on your network.
