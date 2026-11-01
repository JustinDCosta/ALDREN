# ADR-0012: Keep FreeLLMAPI after S8; ALDREN sends only its curated chains

- **Status:** Accepted (2026-11-01). You left this decision to the project; it's recorded on your behalf.
- **Refines:** [ADR-0007](0007-cloud-gateway-freellmapi.md) (its go/no-go). **Supersedes** the "or a pinned model" option in ARCHITECTURE.md §8.3, SECURITY.md and CLAUDE.md.

## Context
S8 ran FreeLLMAPI v0.16.14 with ALDREN's hardening on the target laptop ([results](../benchmarks/phase0.md#s8-freellmapi-gateway)):
- **Configured as ALDREN would use it** (the two `aldren-*` chains, keys enabled only for Google, Groq and OpenRouter):
  - every answered tool call was valid (38/38), and schema output was 20/20;
  - 63/63 answers came from a provider in the requested chain (the model itself wasn't checked then; the bench checks it now);
  - a canary sent in a prompt was never stored;
  - only those three providers were contacted;
  - it listened on loopback only and refused a foreign `Host`.
- **With 34 keys configured, many of them resellers,** a request for the bare model name `gemini-2.5-flash` went to two resellers (dreamprompting, then electronhub) and never reached Google. A model name selects whichever configured platform sells a model by that name.
- **Chains can silently stay empty:** the dashboard's chain table only saves on "Save changes", so a chain looked configured while the database held nothing.
- **Hung requests aren't cut short:** a hung Gemini request ran the full 60 s provider timeout, then failed without failover because the 8 s failover budget was spent.
- **Not measured:** per-request overhead and failover. Memory was 101–103 MB against a 100 MB target.

None of ADR-0007's revisit triggers (unacceptable overhead, unreliable tool calling, a security problem, an unmaintained project) was hit.

## Decision
Keep FreeLLMAPI as ALDREN's optional cloud gateway, on four conditions:
1. **Chains only.** ALDREN sends only its curated `auto:aldren-*` chain ids, never a bare model name.
2. **ALDREN's providers only.** The gateway keeps enabled keys only for providers ALDREN uses. At every start, the read-only drift check compares each chain's contents and the enabled keys with ALDREN's allowlists, and cloud use stays off until a mismatch is fixed.
3. **Short deadlines.** ALDREN enforces its client-side deadlines (ARCHITECTURE.md §8.3) and launches the gateway with per-provider timeouts of about 15 s (`PROVIDER_TIMEOUT_<PLATFORM>`), so a hung provider fails over instead of holding a request for 60 s.
4. **Measure before Phase 6.** Per-request overhead (≤ 20 ms median) and in-chain failover (10/10) are measured before Phase 6 starts. If overhead misses its target, ADR-0007's fallback is reconsidered.

On speed: the voice fast path is local and never touches the gateway (ADR-0004). Cloud requests reach Groq through it in about 150–550 ms; the gateway itself is a hop on the same PC.

## Alternatives considered
- **Drop FreeLLMAPI for ALDREN-built direct adapters** (ADR-0007's fallback): full control, but months of work with no speed gain, and S8 found nothing that calls for it.
- **Keep allowing pinned model names:** unsafe while resellers share model names, as S8 showed. FreeLLMAPI also has provider-specific model ids, but the chains make them unnecessary.

## Consequences
- **Pinned release:** v0.16.14 (tag commit `5127fee51ba97ad112915ebe227ba13df461a266`), evaluated with your installed Node 24.20; Phase 6 pins a portable Node. Upgrades follow [SECURITY.md](../SECURITY.md#freellmapi-hardening-applied-by-aldren-at-launch): changelog review, a compatibility test and your OK.
- **Phase 6** implements the drift check and the deadlines. S8's `aldren-bench gateway` stays as the regression test.
- **Setup:** configuring the gateway stays a dashboard task for you. ALDREN's setup guide must say to click "Save changes" after editing a chain.
- **Rate limits:** free tiers (Gemini allows about 10 requests a minute per model) need pacing or fallback in ALDREN, never a burst of retries.

## Revisit if
Overhead or failover misses its target before Phase 6, FreeLLMAPI changes how chains or model ids resolve, or the project becomes unmaintained.
