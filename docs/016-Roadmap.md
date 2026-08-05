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
- [x] **Planner agent — real implementation.** Calls the Claude Code CLI directly per [[ADR-0005]] (not the Anthropic API — that blocker turned out to be avoidable). Reads `Project.LocalPath`'s real checkout, produces a description + acceptance criteria or genuine clarifying questions, records real cost/token usage on `Run`. Web/issue-tracker MCP tool access ([[009-MCP]]) still not wired — today it only reads the local checkout.
- [x] **Developer agent — real implementation.** Syncs root branch, creates/reuses a real worktree, runs `ClaudeCliProvider` with file-edit access, commits locally on success.
- [x] **Git agent — real implementation.** Pushes the branch and opens a real PR via `gh pr create`, then removes the worktree once both succeed.
- [x] **Full pipeline validated live, unattended, end-to-end**: a title-only task against `felippedomingos/forge-test-sandbox` went Inbox → (hit a real Planner parse edge case → Blocked → resumed) → Backlog → Todo → Executing (real worktree, real `divide()` function written and committed) → AwaitingPublish → Publishing → Review → Done → real push → real PR (https://github.com/felippedomingos/forge-test-sandbox/pull/1, confirmed via `gh pr view`) → worktree cleanly removed. This is the founder's original vision — title in, working PR out — actually running for the first time.
- [x] **Deploy agent — real implementation.** Executes `Project.PublishRecipe.migrationCommand` inside the task's Worktree. Validated live including the failure path (a mismatched recipe failed with a real traceback, correctly stayed at `AwaitingPublish`; fixed recipe then succeeded, second real PR opened).
- [x] **Prioritizer agent — real implementation.** Was dead code (never invoked) until this pass — `BacklogSchedulerWorkflow` now calls it whenever unprioritized Backlog tasks exist, project-scoped, same `ClaudeCliProvider` mechanism as the other roles.
- [x] **All 5 docs/005-Agents.md roles are real, not stubs.** Nothing left pretending.
- [ ] WebSocket live trace ([[007-ExecutionEngine]] §4) — 2-second polling stands in ([[013-Frontend]] §3), now backed by real trace events (the `events` table is populated for real by every agent activity).
- [ ] Basic AuthN so the API isn't fully open ([[014-Security]] §1) — not urgent while single-user/single-machine, becomes a hard blocker before any second user or the real dedicated server ([[ADR-0004]]) is reachable by anyone else.
- [ ] **`--permission-mode bypassPermissions` is a stopgap**, not the final security posture — acceptable only because Developer/Deploy currently only ever run against the disposable sandbox project. A real project needs [[009-MCP]] §4's per-role tool scoping instead of a blanket bypass ([[014-Security]] §4 gap, restated here since it's now live code, not just a documented risk).

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
