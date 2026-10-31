# Known limitations

These are deliberate, honest limits of the design on this hardware. Update this file when measurements (Phase 0 onwards) change them.

## Hardware
- **One resident model.** ~5 GB of usable VRAM fits one ~4B multimodal model with modest context, not three separate local brains. Switching to the 9B tier takes about 4.4 s (measured in S1) and evicts the resident model.
- **No reliable tools while gaming.** With the GPU unloaded, the CPU fallback model can chat but gets tool calls right only 40–67% of the time, so only the deterministic fast path runs commands.
- **No large models on the CPU while you work.** ~7.7 GB of RAM is free during normal use, so 20–35B mixture-of-experts models don't fit, and dense 9B+ models with CPU layers would make screenshot analysis take 20+ s.
- **Battery.** A loaded GPU model keeps the NVIDIA GPU awake. On battery, models unload when idle, so the first reply afterwards is slower.
- **Docked means the GPU is always on.** The external monitor is driven by the NVIDIA GPU.

## Speed and quality
- Voice replies take ~1–2 s with the model warm (target ≤1.5 s typical), simple commands ~0.5–0.8 s, anything that looks at the screen 2–4 s, and research 5–15 s. Instant answers to everything aren't realistic locally.
- A 4B model handles 1–5-step tasks well when UI Automation exposes the controls. Long autonomous tasks across several apps will be unreliable locally.
- These are estimates; Phase 0 measurements replace them. Measured so far (S1): the resident 4B starts answering ~0.11 s after a request with its prompt cached, writes ~75 tokens/s, and answers a question about a screenshot in ~1 s.
- On the eval set the 4B picks the right brain 95% of the time and makes a valid tool call 97% of the time, so roughly one request in twenty to thirty needs a retry or a correction.

## Voice
- No wake-word detector guarantees zero false activations. The target is ≤0.5 false wakes per hour; "Aldrin", "Alden" and "all then" are known near-misses.
- Bluetooth headset microphones drop your headphones into low-quality hands-free audio, so ALDREN avoids them by default.
- On speakers, interrupting ALDREN mid-sentence needs echo cancellation (Phase 9). Until then, the mic is muted while ALDREN speaks, except for "stop" and the hotkey.
- While listening is on, Windows shows the microphone-in-use icon permanently.
- The French text-to-speech voices are weaker than the English ones. The Kokoro model installed for S3 has no French voice at all, and sherpa-onnx 2.0 no longer ships a phonemiser, so French speech needs another model and phonemiser (decision pending).
- On the CPU, Kokoro takes ~0.65 s to start speaking even a short reply (measured in S3), so instant spoken answers need pre-recorded acknowledgements or synthesis on the GPU.

## Windows control
- ALDREN cannot control elevated (admin) windows or UAC prompts, by design.
- Apps with poor accessibility data (custom-drawn UIs, some Electron apps, games) fall back to OCR and vision, which are slower and less precise.
- ALDREN never sends input to games (anti-cheat such as Riot Vanguard could flag it).

## Focus mode
- Enforcement adds friction and accountability; you can always close ALDREN or use another browser or device.
- Website blocking is strongest in Chrome and Edge (through the extension); other browsers only get window-level nudges.

## Webcam
- Presence is not attention, and the webcam only works with the laptop lid open.

## Cloud and the FreeLLMAPI gateway
- Free tiers change without notice and have no SLA. Gemini cut its free quotas by ~90% in Dec 2025, Picovoice ended its free tier in June 2026, and a user reported on 2026-10-31 that Hugging Face had ended its free plan.
- FreeLLMAPI's "7.4 billion tokens/month" is its catalog's total across 34 providers. Your capacity with a handful of providers is a small fraction of that. The best models have the smallest daily caps, so quality drops later in the day and resets at midnight UTC (01:00 in Paris in winter, 02:00 in summer).
- Gateway failover adds latency, and streaming can still fail after the first token.
- Running the gateway locally does not make prompts private: they still go to the upstream providers under their terms.
- The gateway appears to drop the source links from Gemini's Google Search grounding (to confirm in Phase 0). Grounded answers with sources therefore need a direct Gemini adapter and, for Gemini 3+, the paid tier.
- Terms of service: Gemini's March 2026 terms describe the API as for professional or business purposes; Cohere forbids personal use (excluded); NVIDIA NIM is evaluation-only.

## Integrations
- Google shows an "unverified app" warning for your personal OAuth client. Leaving it in "Testing" would expire logins after 7 days, so it is published "In production" (unverified, up to 100 users).
- Classic remote debugging of your everyday Chrome profile has been blocked since Chrome 136, so ALDREN uses its own extension instead.
- Reading Wi-Fi network names requires Windows' "Let desktop apps access your location" setting (currently on). Home detection doesn't depend on it.
