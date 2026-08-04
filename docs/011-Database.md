# 011 — Database

## Status

Not started — Phase 3 (Architecture)

## Purpose

Persistence model for Forge itself (not the projects it operates on): schema, event sourcing approach, and choice of PostgreSQL over SQLite.

## Planned Outline

- Core tables: Projects, Tasks, SubTasks, Workers, Events, Prompts, Responses, Logs, Models, Runs, Artifacts, PRs, Deployments, Plugins, Configurations, Users
- Event sourcing scope (full event sourcing vs. task history only)
- Migration strategy
- Multi-tenancy considerations (if Forge itself is ever offered as SaaS)
