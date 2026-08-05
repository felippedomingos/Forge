# 016 — Roadmap

## Status

Draft — updated 2026-08-05, after the Review-rework / self-hosted-restart / production-polling pass

## MVP

- [x] Docs 000-Vision, 001-Requirements, 002-Architecture, 003-Domain, 004-Workflow
- [x] ADR-0001..0004 (workflow engine, Git provider, model router, infrastructure)
- [x] Kanban board (read + the 4 human-gated actions) — [[013-Frontend]]
- [x] Task state machine as a real Temporal workflow — `TaskWorkflow` in `backend/src/Forge.Workflows`, exercised end-to-end against local Postgres+Temporal
- [x] REST API first slice (projects, tasks, answers/move/promote) — [[012-API]]
- [x] `BacklogSchedulerWorkflow` — one per project, polling every 5s, promotes the top-priority Backlog task when a worker slot is free. Validated live: a task moved Backlog->Todo->Executing->AwaitingPublish with zero manual intervention. Known simplification: fixed-interval polling instead of event-driven wakeup ([[006-Scheduler]] §1) - `ContinueAsNewAsync` since added (see below), the history-limit bug it was protecting against wasn't hypothetical.
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
- [x] **`restartTargets`/`healthCheckUrl` in `PublishRecipe` implemented** ([[015-Deployment]] §2-3) — each `restartTargets` entry is a raw shell command (not a bare Docker Compose service name as first documented - changed after Forge's own self-hosted dev processes didn't fit that assumption), then health-check polling, both gated on the trust flag above. **Validated live** against Forge's own self-hosted restart (`scripts/restart-forge-dev.sh`) - Forge.Worker's PID confirmed unchanged, Forge.Api/frontend cleanly restarted, `GET /health` (new, unauthenticated) returned `200` after.
- [x] **AuthN — JWT bearer auth, admin-created accounts** ([[adr/ADR-0006]], [[014-Security]] §1). Global default-deny (every endpoint requires a valid token except `/auth/login`/`/auth/bootstrap`), `POST /users` is Admin-only, WebSocket auth via `?access_token=`. Validated live end-to-end (bootstrap, login, wrong-password rejection, re-bootstrap blocked, authenticated WS handshake, logout). No refresh-token rotation and no password-reset flow yet - deliberate v1 simplifications, not oversights.
- [x] **`Review → Todo` rework path** ([[004-Workflow]] row 14/§3a, founder-requested) — a reviewer can send a task back for another Developer pass with a comment instead of only approving forward. Found necessary live: a task reached `Review` with nothing actually deployed to inspect, and "approve blindly" was the only option.
- [x] **`Done → Production` confirmation resolved via PR-merge polling** ([[015-Deployment]] §4, founder-requested) — no more undesigned webhook gap; `TaskWorkflow` polls the task's own captured `PullRequestUrl` every 60s (`gh pr view`/`az repos pr show`) and advances automatically once merged.
- [x] **`BacklogSchedulerWorkflow` history-limit bug found and fixed live** ([[006-Scheduler]] §1) — a real project's scheduler ran ~19h at the 5s poll interval, hit Temporal's 51,200-event history limit, and was terminated by the server; the exact "no `ContinueAsNewAsync` yet" risk this doc already flagged, now closed via `Workflow.ContinueAsNewSuggested`. `POST /projects/{id}/resume-scheduler` added as the general recovery lever (mirrors `/tasks/{id}/resume`) - also used to discover and fix the "Forge" project's own scheduler, which had never started at all (the same row/workflow-start race `/tasks/{id}/resume` exists for).
- [ ] Azure DevOps push/PR is implemented against `az repos pr create`'s documented shape but **not yet exercised against a real PR** ([[010-Plugins]] §5) — founder testing this against a real project next.

### What's left after that

Everything else in this MVP section is done. What remains, roughly in order a founder-only, single-machine deployment would hit it:

- **Per-project shell/write trust is coarse** (`Project.AllowAgentBypassPermissions` is one bool, not scoped tool-by-tool) — fine today, revisit only if a project needs "can write files but not run arbitrary shell" or similar finer distinctions ([[009-MCP]] §4, [[014-Security]] §4).
- **Forge changing its own Worker/workflow code needs a manual restart** — the self-restart script deliberately can't touch the process it runs inside of ([[015-Deployment]] §5). A real limitation of self-hosting, not hidden.
- **AuthZ is a single coarse Admin/non-Admin check**, no per-project permissions - fine for one small team, would need real design work before a second organization/tenant ever uses this.
- **`Event.Actor` attribution to real users is only done for 2 of several human-originated endpoints** ([[014-Security]] §6) - the pattern's proven, just not repeated everywhere yet.
- **Real dedicated infrastructure** ([[ADR-0004]]) still doesn't exist - Forge runs entirely on the founder's own machine, substituting for it.
- Everything in v2/v3/v4 below - none of it is blocking, none of it has been asked for yet.

## v2

- Real dedicated infrastructure ([[ADR-0004]]) replacing the local machine, once the MVP items above are proven there first.
- MCP servers beyond the v1 set: `az cli`, browser/Playwright ([[009-MCP]] §3).
- Secrets storage mechanism resolved ([[014-Security]] §2), once a plugin actually needs real credentials.
- Diff/commits view in the task detail panel.

## v3

- Multi-provider Model Router (a second LLM provider implementing the `Provider` interface, [[ADR-0003]]).
- Multi-worker horizontal scaling (already designed for in [[002-Architecture]] §4 — Workers are stateless w.r.t. which tasks they pick up, so this is additive, not a rearchitecture).
- Per-project configurable concurrency limits exposed in the frontend ([[006-Scheduler]] §5).

## v4

- Dashboard / analytics / cost reporting beyond the raw `Run` cost endpoint ([[012-API]] §2).
- Whatever Forge's actual usage surfaces as the next real bottleneck — deliberately not speculated on further here; this roadmap gets updated from evidence, not extended from imagination.
