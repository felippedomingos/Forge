# Architecture Decision Records

This folder holds every significant, hard-to-reverse architectural decision made on Forge, following the lightweight ADR format (Michael Nygard style).

## When to write one

Write an ADR when a decision would be expensive to reverse later, or when a reasonable person could have chosen differently and future contributors will ask "why did we do it this way?" Examples: choice of workflow engine, choice of database, Linux-only execution runtime, event-driven vs. polling orchestration.

Do not write an ADR for reversible implementation details (variable naming, folder layout inside a single module).

## Naming

`ADR-NNNN-short-title.md`, numbered sequentially, never reused.

## Status values

`Proposed` → `Accepted` → `Superseded by ADR-NNNN` (or `Rejected`)

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
