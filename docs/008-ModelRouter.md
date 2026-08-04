# 008 — Model Router

## Status

Not started — Phase 3 (Architecture)

## Purpose

How Forge selects which LLM serves a given agent invocation, based on required capability, cost ceiling, latency and availability, without hardcoding a single vendor.

## Planned Outline

- Routing inputs (agent role, task complexity signal, cost budget, provider availability/rate limits)
- Fallback and failover between providers
- Per-project/per-agent model overrides
- Cost tracking integration (see [[012-API]] cost endpoints)
