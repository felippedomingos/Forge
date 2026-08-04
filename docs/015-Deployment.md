# 015 — Deployment

## Status

Not started — Phase 3/4

## Purpose

How Forge itself is deployed (bare-metal/Linux, Docker), and how the Deploy agent executes a project's own publish step (code, DB migrations, other local changes) per UC-11.

## Planned Outline

- Forge's own deployment topology — dedicated Linux server/VM isolated from Actiz QA/PROD/PROD-LATIN (see [[ADR-0004]]); **blocked until that infrastructure is provisioned**
- Deploy agent's publish protocol (migrations, restarts, health checks, rollback)
- CI/CD pipeline integration for the Production confirmation step (UC-13)

## Blocker

Per [[ADR-0004]], no real Deploy-agent end-to-end test can run until the dedicated VM exists and is reachable (SSH, Temporal server per [[ADR-0001]], PostgreSQL). This is an external action, not something an in-repo agent can resolve on its own.
