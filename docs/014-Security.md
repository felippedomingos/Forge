# 014 — Security

## Status

Not started — Phase 3 (Architecture)

## Purpose

Secrets handling, credential isolation per project/worker, and the blast radius of an agent with shell/Git/cloud CLI access.

## Planned Outline

- Secrets storage and injection into workers
- Per-project credential isolation
- Agent permission boundaries (what an agent can and cannot execute unattended)
- Audit trail requirements tying into [[003-Domain]] event catalog
