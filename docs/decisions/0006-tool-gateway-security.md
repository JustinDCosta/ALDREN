# ADR-0006: Tool Gateway with risk tiers and untrusted-content quarantine

- **Status:** Accepted (2026-10-31)

## Context
ALDREN can operate apps, files, the browser and online accounts. Models make mistakes, and the content they read can manipulate them (prompt injection).

## Decision
- Models only propose typed tool calls. A deterministic Tool Gateway performs schema validation, a permission check, a policy check, confirmation, execution with a timeout, post-condition verification and audit logging.
- Five risk tiers (read, reversible, impactful, destructive or external, forbidden) define the confirmation rules; see [SECURITY.md](../SECURITY.md).
- Content from the web, documents, the screen and the clipboard is tagged untrusted and processed without tool access. While it is present, every tier-2+ action needs confirmation.
- No arbitrary shell, and ALDREN never elevates.

## Alternatives considered
- **Letting the model call OS APIs or a shell directly:** fast to build, unacceptable risk.
- **Confirmation for everything:** safe but unusable.

## Consequences
- Every new capability is a tool with a schema, tier, permission, timeout and tests.
- The design follows published research on securing LLM agents against prompt injection (design patterns that separate untrusted content from tool use, as in CaMeL).

## Revisit if
Real use shows a tier is too strict or too lax. Adjust it per tool; never bypass the gateway.
