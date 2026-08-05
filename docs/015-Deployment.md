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

**Implemented** (not just proposed): stored as a JSONB column, `Project.PublishRecipe`, matching `Plugin.Configuration`'s pattern rather than an EF owned type — schema can grow without a migration each time. Only `migrationCommand` is actually executed by `AgentActivities.DeployAsync`; `restartTargets` and `healthCheckUrl` are accepted by the shape but not exercised — no test project has a real running service to restart or poll yet, and implementing that logic against nothing to verify it against would be guessing at a shape, not building one.

**Idempotency is the recipe author's responsibility, not something Forge enforces.** [[006-Scheduler]] §3 already caps Deploy activity retries at 2 attempts specifically because a partially-applied deploy shouldn't be retried blindly — but if a project's `MigrationCommand` isn't itself safe to run twice, that's a property of how that project's migrations are written, not something the Deploy agent can guarantee generically.

**Cleanliness is also the recipe author's responsibility — found live, not theorized.** A `migrationCommand` of `python3 -m py_compile calculator.py` left a `__pycache__/` directory behind in the worktree. That's an untracked file, so the Git agent's worktree removal ([[005-Agents]] §6) correctly *refused* to delete it afterward (`git worktree remove` without `--force`) rather than silently discarding it — exactly the safety behavior [[007-ExecutionEngine]] §2 and the project's `.claude/settings.json` "ask" list intend. The task still completed successfully (PR opened); only the worktree directory was left for manual cleanup. A recipe that produces artifacts should clean up after itself (or the project's `.gitignore` should exclude them) — Forge won't force-delete on a recipe author's behalf.

## 3. Deploy Agent's Publish Protocol

`migrationCommand` (if set) runs inside the **task's Worktree** (the branch under review — not the canonical clone, since that's where the actual change under test lives), via `/bin/bash -c`. Success → `DeployCompleted` with captured stdout. Failure → `DeployFailed` with captured stderr, which per [[004-Workflow]] §5 bounces the task back to `AwaitingPublish` rather than auto-retrying — **validated live**: a recipe accidentally mismatched to a different task's branch failed with a real Python traceback, correctly stayed at `AwaitingPublish`, and a corrected recipe then succeeded on retry. `restartTargets`/`healthCheckUrl` are not implemented (see above).

## 4. CI/CD Integration for Production Confirmation

[[003-Domain]] row 10 (`PipelineConfirmedDeployment`) is an external event — some CI/CD pipeline watching the pushed branch/PR from the Git agent ([[005-Agents]] §6) eventually reports success, and that report needs to reach the task's Temporal workflow as the `ConfirmProductionAsync` signal (`TaskWorkflow` in `backend/src/Forge.Workflows`). The concrete integration (a webhook receiver in [[012-API]], polling a pipeline's API, or something else) is not designed yet — no CI/CD system is wired to Forge's own repository in a way that could inform this decision today.

## 5. Open Questions

- Exact storage shape for `PublishRecipe` (§2) once a second real project needs one — the JSONB-on-Project proposal above is a reasonable default, not a decision that's been stress-tested against a second use case.
- The CI/CD webhook mechanism (§4) is undesigned; revisit once Forge's own repository has a real pipeline to integrate against.
- No API endpoint exists yet to configure a `PublishRecipe` — it was set directly via SQL for this validation. `POST /projects` ([[012-API]]) should probably accept it, or a dedicated `PATCH /projects/{id}/publish-recipe`.
- A worktree left behind after a refused (dirty) removal has no automatic detection/alerting — a human currently has to notice and clean it up manually, as happened during this validation.
