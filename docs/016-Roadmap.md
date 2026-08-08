# 016 — Roadmap

## Status

Draft — updated 2026-08-08, after the SlayZone-import / global-watchdog / per-project-credentials / multi-account-failover pass

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

### Post-MVP: Forge is now dogfooding itself and running real, non-Forge projects (2026-08-06 through 2026-08-08)

The MVP checklist above hasn't changed since 2026-08-05 - everything since then has been the product actually being used (Forge's own backlog run through its own pipeline autonomously) plus real operational incidents found live and fixed, not new planned features. Roughly in the order they happened:

- **Full task lifecycle self-hosted**: `Project.LocalPath` for Forge itself IS this same checkout - Deploy merges a task's branch into it and restarts Forge's own dev processes (`scripts/restart-forge-dev.sh`) before Review, so "Testar" actually shows the real change, not stale code ([[015-Deployment]] §2/§3a).
- **AI-driven merge-conflict resolution on that merge** ([[015-Deployment]] §3a) - Claude Code CLI resolves a real `git merge` conflict in `LocalPath`, verified via `git diff --check`/`git status` plus an optional per-project build+test gate before Forge commits it.
- **`GitFinalizeAsync` recognizes "already integrated, no PR possible"** ([[015-Deployment]] §3b) instead of leaving a task stuck in `Done` forever - `git merge-base --is-ancestor` checked before attempting PR creation.
- **Global stuck-task watchdog, independent of any project's own scheduler** ([[006-Scheduler]] §4a/§4b) - found live that the original per-project safeguard's uptime was entirely coupled to that project's `BacklogSchedulerWorkflow` staying alive; one global instance now restarts dead schedulers and sweeps every project's stuck tasks on a fixed interval regardless.
- **Fixed a real race in the Todo→Executing capacity gate** ([[006-Scheduler]] §2) - the check-then-act gate could let two tasks for the same project both pass a stale capacity check simultaneously; now an atomic Postgres advisory-lock claim.
- **`git fetch`/`git worktree add` failures go straight to `Blocked`, not a crash-then-retry loop** ([[005-Agents]] §4) - found live burning real Planner cost in a repeating cycle when a project's git credential expired.
- **Per-project GitHub/Azure DevOps credential** ([[010-Plugins]] §6) - `Project.GitCredential`, actively injected into `git fetch`/`push`/PR creation, instead of relying on whatever happened to already be configured on the host.
- **Multi-account Claude failover with real usage tracking** ([[adr/ADR-0005]]) - `ClaudeAccount` (linked to a `User`, token from `claude setup-token`) with automatic rotation on a detected usage-limit failure, and per-account session/weekly usage estimates from real `Run` history.
- **Project memory strengthened automatically, not just read** ([[005-Agents]] §7) - `DevelopAsync` records a memory note when a task reveals something genuinely worth a future task knowing; memory itself is now read by every LLM-invoking activity (Planner, Developer, Prioritizer, Deploy's conflict resolution), not just two of five.
- **SlayZone historical import** (252 tasks, 8 external projects, migrated directly into Postgres bypassing the Planner) - exposed and fixed a real gap: `/tasks/{id}/resume` can now skip planning for a task that already has a real, curated `Description`, and `BacklogSchedulerWorkflow` starting for the first time on a project with a large existing backlog can burst-promote past its own capacity limit before the gate catches up (mitigated by the capacity-gate race fix above).
- **Frontend**: task detail panel actually reaches half the screen width (a CSS specificity bug silently defeated the original intent), Markdown rendering for task descriptions (previously showed literal `##`/`**` from the Planner's real output), sidebar collapse (both the Projects list and the whole drawer), optimistic UI for every card move (the first fix patched the query cache directly and still lost to the board's own 2s poll - the real fix keeps the override outside the cache entirely).

### What's left after all of that

- **Per-project shell/write trust is coarse** (`Project.AllowAgentBypassPermissions` is one bool, not scoped tool-by-tool) — fine today, revisit only if a project needs "can write files but not run arbitrary shell" or similar finer distinctions ([[009-MCP]] §4, [[014-Security]] §4).
- **Forge changing its own Worker/workflow code needs a manual restart** — the self-restart script deliberately can't touch the process it runs inside of ([[015-Deployment]] §5). A real limitation of self-hosting, not hidden.
- **AuthZ is a single coarse Admin/non-Admin check**, no per-project permissions - fine for one small team, would need real design work before a second organization/tenant ever uses this.
- **`Event.Actor` attribution to real users is only done for 2 of several human-originated endpoints** ([[014-Security]] §6) - the pattern's proven, just not repeated everywhere yet.
- **Real dedicated infrastructure** ([[ADR-0004]]) still doesn't exist - Forge runs entirely on the founder's own machine, substituting for it. The fully-containerized `docker-compose.yml` (all 6 services, added 2026-08-07) is documented but has never actually been run end-to-end - the bare-metal dev setup (`STARTUP.md`) is what's validated live.
- **Claude usage-limit detection is unvalidated against a real failure** (`ClaudeCliProvider.IsUsageLimitError`) - a best-effort phrase match, since only one Claude account exists today to test against.
- **`conflictResolutionVerifyCommand` still hasn't been exercised against a resolution that actually needed it to catch a bad fix** - every real conflict so far passed the baseline git-level checks cleanly.
- **Real operational backlog, not a code gap**: as of 2026-08-08, 49 tasks sit `Blocked` across two projects - 26 on AOPS (paused pending a Git credential rotation only the founder can do) and 23 on MKT (genuine clarifying questions needing the founder's own answers, e.g. staging environment access, scope decisions). This will always exist at some level - it's the system correctly routing ambiguity to a human, not a bug to fix away.
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
