# 002 — Architecture

## Status

Not started — Phase 3 (Architecture)

## Purpose

The system-level view of Forge: components, their responsibilities, and how events flow between the board, the workflow engine, the scheduler, workers and external tooling via MCP.

## Planned Outline

- Component diagram (Board → Event Bus → Workflow Engine → Scheduler → Workers → LLMs → MCP → Git/Azure/Docker/DB)
- Why event-driven, not agent-polls-board (ADR link)
- Why Linux-only execution runtime (ADR link)
- Deployment topology (single-node MVP vs. multi-worker)
- Failure domains and retry boundaries
- Cross-cutting concerns: observability, cost tracking, audit trail
