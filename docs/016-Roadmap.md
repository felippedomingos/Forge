# 016 — Roadmap

## Status

Draft — updated 2026-08-05, after the project CRUD / drag-and-drop / Azure DevOps pass

## MVP

- [x] Docs 000-Vision, 001-Requirements, 002-Architecture, 003-Domain, 004-Workflow
- [x] ADR-0001..0004 (workflow engine, Git provider, model router, infrastructure)
- [x] Kanban board (read + the 4 human-gated actions) — [[013-Frontend]]
- [x] Task state machine as a real Temporal workflow — `TaskWorkflow` in `backend/src/Forge.Workflows`, exercised end-to-end against local Postgres+Temporal
- [x] REST API first slice (projects, tasks, answers/move/promote) — [[012-API]]
- [x] `BacklogSchedulerWorkflow` — one per project, polling every 5s, promotes the top-priority Backlog task when a worker slot is free. Validated live: a task moved Backlog->Todo->Executing->AwaitingPublish with zero manual intervention. Known simplification: fixed-interval polling instead of event-driven wakeup, no `ContinueAsNewAsync` yet ([[006-Scheduler]] §1).
- [x] **Planner agent — real implementation.** Calls the Claude Code CLI directly per [[ADR-0005]] (not the Anthropic API — that blocker turned out to be avoidable). Reads `Project.LocalPath`'s real checkout, produces a description + acceptance criteria or genuine clarifying questions, records real cost/token usage on `Run`. Web/issue-tracker MCP tool access ([[009-MCP]]) still not wired — today it only reads the local checkout.
- [x] **Developer agent — real implementation.** Syncs root branch, creates/reuses a real worktree, runs `ClaudeCliProvider` with file-edit access, commits locally on success.
- [x] **Git agent — real implementation.** Pushes the branch and opens a real PR via `gh pr create`, then removes the worktree once both succeed.
- [x] **Full pipeline validated live, unattended, end-to-end**: a title-only task against `felippedomingos/forge-test-sandbox` went Inbox → (hit a real Planner parse edge case → Blocked → resumed) → Backlog → Todo → Executing (real worktree, real `divide()` function written and committed) → AwaitingPublish → Publishing → Review → Done → real push → real PR (https://github.com/felippedomingos/forge-test-sandbox/pull/1, confirmed via `gh pr view`) → worktree cleanly removed. This is the founder's original vision — title in, working PR out — actually running for the first time.
- [x] **Deploy agent — real implementation.** Executes `Project.PublishRecipe.migrationCommand` inside the task's Worktree. Validated live including the failure path (a mismatched recipe failed with a real traceback, correctly stayed at `AwaitingPublish`; fixed recipe then succeeded, second real PR opened).
- [x] **Prioritizer agent — real implementation.** Was dead code (never invoked) until this pass — `BacklogSchedulerWorkflow` now calls it whenever unprioritized Backlog tasks exist, project-scoped, same `ClaudeCliProvider` mechanism as the other roles.
- [x] **All 5 docs/005-Agents.md roles are real, not stubs.** Nothing left pretending.
- [x] WebSocket live trace ([[007-ExecutionEngine]] §4) — real, not a stub: Postgres `LISTEN`/`NOTIFY` bridging the Worker to the API's in-memory connections, validated live with sub-second delivery. Board-wide list still polls every 2s (only the open task detail panel has the socket) — tracked below, not a real gap yet at this scale.
- [x] Project lifecycle from the UI: create/edit/delete (with cascade + best-effort Temporal workflow termination on delete), shared memory editor, `PublishRecipe.previewUrl` + a "Testar" button on `Review` tasks, a real branch picker (`GET /git/branches`), Azure DevOps as a second selectable git provider.
- [x] Task tags (`{Prefix}-{Number}`), a left project sidebar/tree, an all-tasks cross-project view, a global (estimated) spend indicator, light/dark theme toggle, task creation notes that the Planner fetches URLs from.
- [x] Drag-and-drop actually works end-to-end (was reported broken — the whole card is the drag surface now, not a tiny hover-only handle) — [[013-Frontend]] §3.
- [x] **`Project.AllowAgentBypassPermissions` trust gate** — founder-requested simplification of the per-role MCP scoping idea into one per-project bool. Developer/Deploy both refuse outright (`Blocked`/`DeployFailed` with an explicit reason) for any project not explicitly marked trusted; false by default on new projects. `Scheduler Test Project` (the sandbox) marked trusted since it's been running write operations all session; `Forge` (the real repo) stays untrusted.
- [x] **`restartTargets`/`healthCheckUrl` in `PublishRecipe` now implemented** ([[015-Deployment]] §2-3) — `docker compose restart` per target, then health-check polling, both gated on the trust flag above. Built correctly, not yet exercised against a real long-running service (no current project has one configured).
- [ ] **Basic AuthN so the API isn't fully open** ([[014-Security]] §1) — still nothing here, still the single largest gap between "works for me" and "works for a team." **Founder has confirmed this should be built next** (real multi-user need) — scope (session-based vs token, how `User.Role` maps to permissions) to be nailed down before implementation starts.
- [ ] Azure DevOps push/PR is implemented against `az repos pr create`'s documented shape but **not yet exercised against a real PR** ([[010-Plugins]] §5) — founder plans to test this against a real project shortly.

## v2

- Real dedicated infrastructure ([[ADR-0004]]) replacing the local machine, once the MVP items above are proven there first.
- MCP servers beyond the v1 set: `az cli`, browser/Playwright ([[009-MCP]] §3).
- Secrets storage mechanism resolved ([[014-Security]] §2), once a plugin actually needs real credentials.
- CI/CD → `PipelineConfirmedDeployment` integration ([[015-Deployment]] §4) — still undesigned, no real pipeline to integrate against yet.
- Diff/commits view in the task detail panel.

## v3

- Multi-provider Model Router (a second LLM provider implementing the `Provider` interface, [[ADR-0003]]).
- Multi-worker horizontal scaling (already designed for in [[002-Architecture]] §4 — Workers are stateless w.r.t. which tasks they pick up, so this is additive, not a rearchitecture).
- Per-project configurable concurrency limits exposed in the frontend ([[006-Scheduler]] §5).

## v4

- Dashboard / analytics / cost reporting beyond the raw `Run` cost endpoint ([[012-API]] §2).
- Whatever Forge's actual usage surfaces as the next real bottleneck — deliberately not speculated on further here; this roadmap gets updated from evidence, not extended from imagination.
