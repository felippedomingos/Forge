# 006 — Scheduler

## Status

Not started — Phase 3 (Architecture)

## Purpose

How events are turned into actual agent invocations: queuing, concurrency limits per project/worker, retry policy, and backpressure.

## Planned Outline

- Event consumption model
- Concurrency limits (per project, per worker, global)
- Retry and backoff policy
- Priority handling (interaction with the Prioritizer agent's ordering)
- Temporal workflow/activity mapping (see [[ADR-0001]] — decided, not open)
