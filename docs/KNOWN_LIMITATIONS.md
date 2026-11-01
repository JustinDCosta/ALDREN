# Known limitations

These are deliberate, honest limits of the design on this hardware. Update this file when measurements (Phase 0 onwards) change them.

## Hardware
- **One resident model.** ~5 GB of usable VRAM fits one ~4B multimodal model with modest context, not three separate local brains. Switching to the 9B tier takes about 4.4 s (measured in S1) and evicts the resident model.
- **No reliable tools while gaming.** With the GPU unloaded, the CPU fallback model can chat but gets tool calls right only 40–67% of the time, so only the deterministic fast path runs commands.
- **No large models on the CPU while you work.** ~7.7 GB of RAM is free during normal use, so 20–35B mixture-of-experts models don't fit, and dense 9B+ models with CPU layers would make screenshot analysis take 20+ s.
- **Battery.** A loaded GPU model keeps the NVIDIA GPU awake. On battery, models unload when idle, so the first reply afterwards is slower.
- **Docked means the GPU is always on.** The external monitor is driven by the NVIDIA GPU.

## Speed and quality
Measured in Phase 0 unless marked as an estimate:
- **Voice:** about 0.85–0.9 s from your last word to ALDREN's first sound with the model warm: 400 ms to be sure you've stopped (recognition, started at 250 ms, finishes at about the same moment), ~0.25 s for the model's first sentence (estimated from its speed) and ~0.21 s for the voice. A short pre-made acknowledgement can start at ~0.4 s. The whole chain is first timed end to end in Phase 3.
- **Text:** the resident 4B starts answering ~0.11 s after a request with its prompt cached and writes ~75 tokens/s. A question that needs the 9B first waits ~4.4 s for the switch.
- **Screen:** a question about a screenshot takes ~1 s. Finding a control by vision on a real window takes 3–5 s and was right only about one time in four, so ALDREN relies on UI Automation's names wherever it can ([ADR-0013](decisions/0013-perception-names-first.md), proposed).
- **Research:** 5–15 s is still an estimate; Phase 6 measures it.
- On the eval set the 4B picks the right brain 95% of the time and makes a valid tool call 97% of the time, so roughly one request in twenty to thirty needs a retry or a correction.
- A 4B model should handle 1–5-step tasks when UI Automation exposes the controls (Phase 5 measures this). Long autonomous tasks across several apps will be unreliable locally.

## Voice
- No wake-word detector guarantees zero false activations. The target is ≤0.5 false wakes per hour. The current model woke about 16 times per hour of your normal use in Phase 0, so the wake word isn't usable yet; training continues before Phase 3, and the hotkey works meanwhile.
- "Aldrin" sounds exactly like ALDREN as you say it, so it will always wake it; "Alden" and "all then" are known near-misses.
- Bluetooth headset microphones drop your headphones into low-quality hands-free audio, so ALDREN avoids them by default.
- On speakers, interrupting ALDREN mid-sentence needs echo cancellation (Phase 9). Until then, the mic is muted while ALDREN speaks, except for "stop" and the hotkey.
- While listening is on, Windows shows the microphone-in-use icon permanently.
- ALDREN speaks English only (your decision, 2026-11-01); it can still understand some French, but recognition makes about one error in two French words.
- The voice (Supertonic 3) starts ~0.2 s after a sentence is ready. It generates a whole sentence at once, so replies are spoken sentence by sentence and very long first sentences start later.
- A pause of 300 ms or more in the middle of a command can still look like the end of it; ALDREN waits 400 ms, which caught every pause in your recordings.

## Windows control
- ALDREN cannot control elevated (admin) windows or UAC prompts, by design.
- Apps with poor accessibility data fall back to OCR and vision, which are slower and much less precise. In Phase 0, Discord, Steam and Epic exposed no controls to UI Automation (VS Code and Claude only 4), Windows OCR read about two thirds of the UI labels, and the 4B model found a named control among dozens about one time in four. Phase 5 works on this ([ADR-0013](decisions/0013-perception-names-first.md), proposed).
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
- The gateway doesn't cut a hung provider short: in Phase 0 one Gemini request held for its full 60 s timeout and then failed without failover. ALDREN therefore sets ~15 s provider timeouts and its own deadlines.
- Free tiers rate-limit bursts: Gemini allows about 10 requests a minute per model, and a burst drew 429 errors. ALDREN paces requests or falls back within the chain rather than retrying.
- The gateway uses about 100 MB of RAM while it runs (101–103 MB measured).
- Running the gateway locally does not make prompts private: they still go to the upstream providers under their terms.
- The gateway drops the source links from Gemini's Google Search grounding: its Gemini adapter never reads the grounding metadata, and the Phase 0 request came back without links. Grounded answers with sources therefore need a direct Gemini adapter and, for Gemini 3+, the paid tier.
- A model name doesn't pin a provider. Any configured platform that sells a model under that name can serve it: in Phase 0 a request for `gemini-2.5-flash` went to two resellers (dreamprompting, then electronhub) instead of Google. ALDREN therefore only uses its curated chains, and the gateway holds keys only for providers ALDREN may use.
- Terms of service: Gemini's March 2026 terms describe the API as for professional or business purposes; Cohere forbids personal use (excluded); NVIDIA NIM is evaluation-only.

## Integrations
- Google shows an "unverified app" warning for your personal OAuth client. Leaving it in "Testing" would expire logins after 7 days, so it is published "In production" (unverified, up to 100 users).
- Classic remote debugging of your everyday Chrome profile has been blocked since Chrome 136, so ALDREN uses its own extension instead.
- Chrome 156 ignores `--load-extension`, so the companion extension is installed by hand (Developer mode) or from the Web Store. Its clicks are synthetic and some sites ignore them; Playwright's are real input.
- Reading Wi-Fi network names requires Windows' "Let desktop apps access your location" setting (currently on). Home detection doesn't depend on it.
