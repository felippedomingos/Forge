# 015 — Deployment

## Status

Not started — Phase 3/4

## Purpose

How Forge itself is deployed (bare-metal/Linux, Docker), and how the Deploy agent executes a project's own publish step (code, DB migrations, other local changes) per UC-11.

## Planned Outline

- Forge's own deployment topology — dedicated Linux server/VM isolated from Actiz QA/PROD/PROD-LATIN (see [[ADR-0004]]); **blocked until that infrastructure is provisioned**
- Deploy agent's publish protocol (migrations, restarts, health checks, rollback)
- CI/CD pipeline integration for the Production confirmation step (UC-13)

## Local validation environment (interim)

Per the amendment in [[ADR-0004]], Postgres + Temporal + Temporal UI now run locally via `docker/local/docker-compose.yml`, unblocking architecture validation before the real dedicated server exists. See `docker/local/README.md` for connection details. This is scaffolding for Phase 3/4 validation only — not the target deployment topology.
