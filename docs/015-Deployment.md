# 015 — Deployment

## Status

Draft — Phase 3/4

## 1. Forge's Own Deployment Topology

Dedicated Linux server/VM isolated from Actiz's QA/PROD/PROD-LATIN clusters, per [[ADR-0004]]. **Currently substituted by the founder's local machine** (the amendment in that ADR) — Postgres + Temporal + Temporal UI run via `docker/local/docker-compose.yml` (see `docker/local/README.md` for connection details), unblocking architecture and code validation before the real server exists. This is scaffolding for validation only, not the target topology — when the real server is provisioned, the same container shape moves there per [[002-Architecture]] §4.

## 2. The PublishRecipe Gap, Resolved

[[005-Agents]] §5 and [[016-Roadmap]] both flagged the same open question: how does Forge know *how* to publish a given project locally? This document proposes a concrete answer rather than leaving it open indefinitely.

```
PublishRecipe {
  ProjectId: Guid
  MigrationCommand?: string   // e.g. "dotnet ef database update", run first if present
  RestartTargets: string[]    // e.g. Docker Compose service names to restart, in order
  HealthCheckUrl?: string     // polled after restart; DeployCompleted only fires once it responds
}
```

Stored as a JSONB column on `Project` (or a dedicated `publish_recipes` table if it grows more structure than this) — not modeled as a [[010-Plugins]] `Plugin`, because it isn't a swappable *provider* the way a Git provider or deployment target is; it's per-project operational configuration, closer to `Plugin.configuration` in spirit than to a `Plugin` itself. Concrete schema addition deferred to whenever the real Deploy agent implementation begins ([[016-Roadmap]] MVP item), not added to [[011-Database]]'s migration yet since nothing consumes it today.

**Idempotency is the recipe author's responsibility, not something Forge enforces.** [[006-Scheduler]] §3 already caps Deploy activity retries at 2 attempts specifically because a partially-applied deploy shouldn't be retried blindly — but if a project's `MigrationCommand` isn't itself safe to run twice, that's a property of how that project's migrations are written, not something the Deploy agent can guarantee generically.

## 3. Deploy Agent's Publish Protocol

Once a `PublishRecipe` exists for a project: run `MigrationCommand` (if set) → restart `RestartTargets` in order → poll `HealthCheckUrl` (if set) until it responds or a timeout elapses → `DeployCompleted`. Any step failing at any point → `DeployFailed`, which per [[004-Workflow]] §5 bounces the task back to `AwaitingPublish` rather than auto-retrying further steps.

## 4. CI/CD Integration for Production Confirmation

[[003-Domain]] row 10 (`PipelineConfirmedDeployment`) is an external event — some CI/CD pipeline watching the pushed branch/PR from the Git agent ([[005-Agents]] §6) eventually reports success, and that report needs to reach the task's Temporal workflow as the `ConfirmProductionAsync` signal (`TaskWorkflow` in `backend/src/Forge.Workflows`). The concrete integration (a webhook receiver in [[012-API]], polling a pipeline's API, or something else) is not designed yet — no CI/CD system is wired to Forge's own repository in a way that could inform this decision today.

## 5. Open Questions

- Exact storage shape for `PublishRecipe` (§2) once a second real project needs one — the JSONB-on-Project proposal above is a reasonable default, not a decision that's been stress-tested against a second use case.
- The CI/CD webhook mechanism (§4) is undesigned; revisit once Forge's own repository has a real pipeline to integrate against.
