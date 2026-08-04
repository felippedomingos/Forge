# 010 — Plugin System

## Status

Not started — Phase 3 (Architecture)

## Purpose

The stable extension interface so Git providers, issue trackers, cloud CLIs, databases and deployment targets can be added without modifying the core.

## Planned Outline

- Plugin interface contract (lifecycle hooks, capabilities declared)
- Built-in plugins at v1 (Git, GitHub/Azure DevOps, Docker, MySQL/PostgreSQL)
- Plugin discovery and configuration
- Versioning and compatibility guarantees
