# 010 — Plugin System

## Status

Not started — Phase 3 (Architecture)

## Purpose

The stable extension interface so Git providers, issue trackers, cloud CLIs, databases and deployment targets can be added without modifying the core.

## Planned Outline

- Plugin interface contract (lifecycle hooks, capabilities declared)
- Built-in plugins at v1: GitHub first ([[ADR-0002]]), Azure DevOps deferred, PostgreSQL
- Plugin discovery and configuration
- Versioning and compatibility guarantees
- Azure DevOps plugin implementation doubles as the acceptance test for this interface's genericity (per [[ADR-0002]] consequences)
