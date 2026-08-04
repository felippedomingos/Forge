# 007 — Execution Engine

## Status

Not started — Phase 3 (Architecture)

## Purpose

The runtime that actually executes a Developer/Deploy/Git agent run: worker provisioning, git worktree lifecycle, sandboxing, resource limits, and the live console/trace stream surfaced to the UI.

## Planned Outline

- Worker lifecycle (provision, home directory, teardown)
- Git worktree lifecycle (create, sync root branch, branch naming, delete)
- Sandboxing and resource limits
- Live trace/event streaming to the frontend (per UC-9 in [[000-Vision]])
- Cost and token accounting per run
