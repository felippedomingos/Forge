# 015 — Deployment

## Status

Not started — Phase 3/4

## Purpose

How Forge itself is deployed (bare-metal/Linux, Docker), and how the Deploy agent executes a project's own publish step (code, DB migrations, other local changes) per UC-11.

## Planned Outline

- Forge's own deployment topology
- Deploy agent's publish protocol (migrations, restarts, health checks, rollback)
- CI/CD pipeline integration for the Production confirmation step (UC-13)
