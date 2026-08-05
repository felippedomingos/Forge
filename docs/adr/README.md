# Architecture Decision Records

This folder holds every significant, hard-to-reverse architectural decision made on Forge, following the lightweight ADR format (Michael Nygard style).

## When to write one

Write an ADR when a decision would be expensive to reverse later, or when a reasonable person could have chosen differently and future contributors will ask "why did we do it this way?" Examples: choice of workflow engine, choice of database, Linux-only execution runtime, event-driven vs. polling orchestration.

Do not write an ADR for reversible implementation details (variable naming, folder layout inside a single module).

## Naming

`ADR-NNNN-short-title.md`, numbered sequentially, never reused.

## Status values

`Proposed` → `Accepted` → `Superseded by ADR-NNNN` (or `Rejected`)

## Index

- [ADR-0001](ADR-0001-temporal-as-workflow-engine.md) — Temporal as the workflow/orchestration engine
- [ADR-0002](ADR-0002-github-first-git-provider.md) — GitHub as the first Git/issue-tracker plugin
- [ADR-0003](ADR-0003-claude-only-model-router-v1.md) — Claude-only Model Router for v1
- [ADR-0004](ADR-0004-dedicated-infrastructure.md) — Dedicated infrastructure for Forge
- [ADR-0005](ADR-0005-claude-code-cli-as-invocation-mechanism.md) — Claude Code CLI (interactive auth) as the agent invocation mechanism
- [ADR-0006](ADR-0006-jwt-authentication-admin-created-accounts.md) — JWT authentication, admin-created accounts

## Template

```markdown
# ADR-NNNN: Title

## Status
Proposed | Accepted | Superseded by ADR-NNNN | Rejected

## Context
What forces are at play — technical, business, timeline.

## Decision
What we decided to do.

## Consequences
What becomes easier or harder as a result. Include trade-offs honestly.
```
