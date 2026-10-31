# ADR-0008: ALDREN-owned research pipeline with verified citations

- **Status:** Accepted (2026-10-31)

## Context
The Research Brain must search the web, read sources and answer with citations. An LLM gateway is not a search engine, and Gemini grounding through FreeLLMAPI appears to drop the source links.

## Decision
- Research runs only on your explicit request, or after you accept an offer to look something up.
- **Pipeline:**
  1. plan queries;
  2. search;
  3. fetch and extract pages locally (with size limits and untrusted tagging);
  4. summarise each source with a tool-less model;
  5. synthesise via `auto:aldren-research` with numbered citations;
  6. verify citations: every claim must map to a fetched source, and uncited claims are flagged or removed.
- **Search step:** the gateway's search layer if Phase 0 confirms it can return structured results; otherwise direct adapters (Tavily, Brave, Exa, or a self-hosted SearXNG).
- **Optional quick grounded answers:** through a direct Gemini adapter that keeps the source metadata (needs a Gemini key; grounding on Gemini 3+ needs the paid tier).
- **Privacy:** only the research question, plus context you approve, leaves the PC. ALDREN speaks a short local summary; the UI shows the full answer, its sources and the provider/model used.

## Alternatives considered
- **Grounded answers through the gateway only:** no sources.
- **Provider "compound" systems (models with built-in search):** less control over sources and provenance; usable later as a fallback.

## Consequences
- ALDREN maintains small search adapters and a page fetcher, and research quality becomes testable (citation checks).

## Revisit if
The gateway starts passing grounding sources through; grounded answers could then use it.
