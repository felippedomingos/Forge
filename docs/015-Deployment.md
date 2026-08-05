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
  PreviewUrl?: string         // opened by the founder-requested "Testar" button once a
                              // task reaches Review - purely informational, never read
                              // by DeployAsync itself, see below
}
```

**Implemented** (not just proposed): stored as a JSONB column, `Project.PublishRecipe`, matching `Plugin.Configuration`'s pattern rather than an EF owned type — schema can grow without a migration each time. Only `migrationCommand` is actually executed by `AgentActivities.DeployAsync`; `restartTargets` and `healthCheckUrl` are accepted by the shape but not exercised — no test project has a real running service to restart or poll yet, and implementing that logic against nothing to verify it against would be guessing at a shape, not building one.

**`previewUrl` is deliberately the cheap half of "can I see the result."** The founder asked for a one-click "Testar" button on a `Review` task that opens a browser at the published solution. The complete version of that (Deploy agent actually restarting the service via `restartTargets`, gating on `healthCheckUrl` before ever reaching `Review`) is real, unbuilt work — tracked below, not done here. What's shipped instead: `previewUrl` is a plain human-maintained pointer (set via the project edit dialog, [[013-Frontend]]), and the button just opens it. It's only as trustworthy as whatever actually keeps that URL's service current — today, nothing does that automatically.

**Bug found and fixed while wiring this in**: the `PATCH /projects/{id}/publish-recipe` handler ([[012-API]]) called `JsonSerializer.Serialize(request)` directly, bypassing the API's configured camelCase JSON options (that configuration only applies to the framework's own request/response pipeline, not a manual serializer call) — it silently wrote `PascalCase` keys (`"PreviewUrl"`) into the stored JSON. `AgentActivities.PublishRecipeDto` tolerated it (case-insensitive deserialization), but the frontend's plain `JSON.parse` didn't, so a saved `previewUrl` read back as `undefined`. Fixed by passing `JsonNamingPolicy.CamelCase` explicitly at that one call site.

**Idempotency is the recipe author's responsibility, not something Forge enforces.** [[006-Scheduler]] §3 already caps Deploy activity retries at 2 attempts specifically because a partially-applied deploy shouldn't be retried blindly — but if a project's `MigrationCommand` isn't itself safe to run twice, that's a property of how that project's migrations are written, not something the Deploy agent can guarantee generically.

**Cleanliness is also the recipe author's responsibility — found live, not theorized.** A `migrationCommand` of `python3 -m py_compile calculator.py` left a `__pycache__/` directory behind in the worktree. That's an untracked file, so the Git agent's worktree removal ([[005-Agents]] §6) correctly *refused* to delete it afterward (`git worktree remove` without `--force`) rather than silently discarding it — exactly the safety behavior [[007-ExecutionEngine]] §2 and the project's `.claude/settings.json` "ask" list intend. The task still completed successfully (PR opened); only the worktree directory was left for manual cleanup. A recipe that produces artifacts should clean up after itself (or the project's `.gitignore` should exclude them) — Forge won't force-delete on a recipe author's behalf.

## 3. Deploy Agent's Publish Protocol

`migrationCommand` (if set) runs inside the **task's Worktree** (the branch under review — not the canonical clone, since that's where the actual change under test lives), via `/bin/bash -c`. Success → `DeployCompleted` with captured stdout. Failure → `DeployFailed` with captured stderr, which per [[004-Workflow]] §5 bounces the task back to `AwaitingPublish` rather than auto-retrying — **validated live**: a recipe accidentally mismatched to a different task's branch failed with a real Python traceback, correctly stayed at `AwaitingPublish`, and a corrected recipe then succeeded on retry. `restartTargets`/`healthCheckUrl` are not implemented (see above).

## 4. CI/CD Integration for Production Confirmation

[[003-Domain]] row 10 (`PipelineConfirmedDeployment`) is an external event — some CI/CD pipeline watching the pushed branch/PR from the Git agent ([[005-Agents]] §6) eventually reports success, and that report needs to reach the task's Temporal workflow as the `ConfirmProductionAsync` signal (`TaskWorkflow` in `backend/src/Forge.Workflows`). The concrete integration (a webhook receiver in [[012-API]], polling a pipeline's API, or something else) is not designed yet — no CI/CD system is wired to Forge's own repository in a way that could inform this decision today.

## 5. Open Questions

- Exact storage shape for `PublishRecipe` (§2) once a second real project needs one — the JSONB-on-Project proposal above is a reasonable default, not a decision that's been stress-tested against a second use case.
- The CI/CD webhook mechanism (§4) is undesigned; revisit once Forge's own repository has a real pipeline to integrate against.
- A worktree left behind after a refused (dirty) removal has no automatic detection/alerting — a human currently has to notice and clean it up manually, as happened during this validation.
- `restartTargets`/`healthCheckUrl` remain unimplemented (see above) — the real fix for `previewUrl` potentially pointing at a stale build. Worth doing once a project with an actual long-running service (not just a script) needs `Testar` to be trustworthy by construction rather than by hand.
