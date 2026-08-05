# 016 — Roadmap

## Status

Draft — updated as of the overnight autonomous build session, 2026-08-05

## MVP

- [x] Docs 000-Vision, 001-Requirements, 002-Architecture, 003-Domain, 004-Workflow
- [x] ADR-0001..0004 (workflow engine, Git provider, model router, infrastructure)
- [x] Kanban board (read + the 4 human-gated actions) — [[013-Frontend]]
- [x] Task state machine as a real Temporal workflow — `TaskWorkflow` in `backend/src/Forge.Workflows`, exercised end-to-end against local Postgres+Temporal
- [x] REST API first slice (projects, tasks, answers/move/promote) — [[012-API]]
- [x] `BacklogSchedulerWorkflow` — one per project, polling every 5s, promotes the top-priority Backlog task when a worker slot is free. Validated live: a task moved Backlog->Todo->Executing->AwaitingPublish with zero manual intervention. Known simplification: fixed-interval polling instead of event-driven wakeup, no `ContinueAsNewAsync` yet ([[006-Scheduler]] §1).
- [ ] **Planner agent — real implementation.** Currently a stub that always succeeds with a placeholder description ([[005-Agents]] §2). Needs: Claude Agent SDK wiring through the Model Router ([[008-ModelRouter]]), repository read access, web/issue-tracker MCP tools ([[009-MCP]]). **Blocked on a real prerequisite**: Forge's own backend needs Anthropic API credentials configured for it to call — distinct from any session credential used to build Forge itself.
- [ ] **Developer agent — real implementation.** Currently a stub. Needs: worktree sync/create/reuse ([[007-ExecutionEngine]] §2), the actual agent loop, build/test execution, live trace streaming. Same credential prerequisite as the Planner agent.
- [ ] **Deploy agent — real implementation.** [[015-Deployment]] §2 now proposes a concrete `PublishRecipe` shape — next step is adding the schema and wiring `DeployAsync` to execute it.
- [ ] **Git agent — real implementation.** Currently a no-op stub; needs real GitHub push/PR calls through the plugin interface ([[010-Plugins]] §2).
- [ ] WebSocket live trace ([[007-ExecutionEngine]] §4) — 2-second polling stands in ([[013-Frontend]] §3).
- [ ] Basic AuthN so the API isn't fully open ([[014-Security]] §1) — not urgent while single-user/single-machine, becomes a hard blocker before any second user or the real dedicated server ([[ADR-0004]]) is reachable by anyone else.

## v2

- Real dedicated infrastructure ([[ADR-0004]]) replacing the local machine, once the MVP items above are proven there first.
- Azure DevOps plugin (the real acceptance test for [[010-Plugins]]'s interface genericity, per [[ADR-0002]]).
- MCP servers beyond the v1 set: `az cli`, browser/Playwright ([[009-MCP]] §3).
- Secrets storage mechanism resolved ([[014-Security]] §2), once a plugin actually needs real credentials.
- Task detail view, other frontend navigation sections ([[013-Frontend]] §3).

## v3

- Multi-provider Model Router (a second LLM provider implementing the `Provider` interface, [[ADR-0003]]).
- Multi-worker horizontal scaling (already designed for in [[002-Architecture]] §4 — Workers are stateless w.r.t. which tasks they pick up, so this is additive, not a rearchitecture).
- Per-project configurable concurrency limits exposed in the frontend ([[006-Scheduler]] §5).

## v4

- Dashboard / analytics / cost reporting beyond the raw `Run` cost endpoint ([[012-API]] §2).
- Whatever Forge's actual usage surfaces as the next real bottleneck — deliberately not speculated on further here; this roadmap gets updated from evidence, not extended from imagination.
