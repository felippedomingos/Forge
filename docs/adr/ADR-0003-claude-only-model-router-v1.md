# ADR-0003: Claude-Only Model Router for v1

## Status

Accepted

## Context

[[000-Vision]] mandates a Model Router abstraction ([[008-ModelRouter]]) so no agent role hardcodes a single LLM vendor. Building true multi-provider routing — cost-based failover across Claude, GPT, Gemini — before any agent works end-to-end would delay the first working pipeline in exchange for a capability (provider failover) that has no user yet.

## Decision

v1 ships with the Model Router interface fully abstracted (a `Provider` interface plus a capability/cost metadata schema), but only one concrete provider implemented: Anthropic Claude, via Claude Code / the Claude Agent SDK. Additional providers (GPT, Gemini, others) are added later by implementing the same interface — this is an additive change, not a rework.

## Consequences

- The router abstraction exists from day one; adding a second provider later is a plugin-style addition, not an architecture change.
- Faster path to a working agent — no time spent on cross-provider prompt normalization or failover logic before the core pipeline (Planner → Backlog → Todo → Developer → ...) is validated end-to-end.
- No automatic failover if Claude has an outage or is rate-limited during MVP: a stuck task waits/retries against the same provider rather than falling over to an alternative.
- The actual "routing" decision logic (cost/capability comparison) remains untested in practice until a second provider is implemented — treat the first non-Claude provider as the acceptance test for the `Provider` interface, same caveat as [[ADR-0002]] for the plugin interface.
