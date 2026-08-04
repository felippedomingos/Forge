# 008 — Model Router

## Status

Not started — Phase 3 (Architecture)

## Purpose

How Forge selects which LLM serves a given agent invocation, based on required capability, cost ceiling, latency and availability, without hardcoding a single vendor.

## Planned Outline

- Provider interface + capability/cost metadata schema (implemented for Claude only in v1 — see [[ADR-0003]])
- Routing inputs (agent role, task complexity signal, cost budget, provider availability/rate limits) — designed now, exercised once a second provider exists
- Fallback and failover between providers — deferred past v1 per [[ADR-0003]]
- Per-project/per-agent model overrides
- Cost tracking integration (see [[012-API]] cost endpoints)
