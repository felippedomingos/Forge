# 004 — Workflow

## Status

Not started — Phase 2 (Domain) / Phase 3 (Architecture)

## Purpose

The full task lifecycle state machine, expanded from the high-level flow in [[000-Vision]] §9: every state, every transition, who/what triggers it, guards, timeouts and rollback behavior.

## Planned Outline

- State catalog: Inbox, Backlog, Blocked, Todo, Executing, Awaiting Publish, Publishing, Review, Done, Production
- Transition table (from, to, trigger event, guard, side effects)
- Timeout and escalation rules (e.g. task stuck in Blocked)
- Rollback semantics (failed publish, failed deploy)
- Relationship between Task state and Sub-task state
