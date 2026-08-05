# 007 — Execution Engine

## Status

Draft — Phase 3 (Architecture)

## 1. Worker Lifecycle

A **Worker** ([[003-Domain]]) is a long-lived process that polls a Temporal task queue and hosts the five agent activity implementations ([[005-Agents]]). At v1, Workers are **not** dynamically provisioned per task — they're a fixed pool of containers started alongside the rest of the [[002-Architecture]] stack (e.g. `forge-worker-1`, `forge-worker-2`, ...), matching the founder's original description of each worker having its own home directory. Dynamic worker provisioning (spin up a container per task, tear down after) is a post-MVP optimization tracked in [[016-Roadmap]] — not needed until fixed-pool capacity is demonstrably a bottleneck.

Each Worker process has its own home directory (`/var/forge/workers/worker-N/`) for scratch space, tool config and any per-worker credential cache — isolating one worker's state from another's even though they may run activities for different projects over time.

## 2. Git Worktree Lifecycle

Directly implements [[000-Vision]] UC-8 and the Developer agent's trigger in [[005-Agents]] §4:

1. **Sync**: `git fetch` against the Project's `root_branch` (`main`/`develop`/`dev` per [[003-Domain]]).
2. **Create**: `git worktree add <path> -b <branch-name> <root_branch>`, where:
   - `path` = `<project_worktrees_dir>/task-<task_id>/`
   - `branch-name` = `forge/task-<task_id>-<slug-of-title>`
3. **Work**: the Developer agent operates entirely inside `path` — this is the enforcement mechanism behind [[003-Domain]] INV-2 (at most one active Worktree per Task), since a second worktree for the same task would collide on the same path.
4. **Delete**: `git worktree remove <path>` by the Git agent once push+PR succeed ([[005-Agents]] §6), emitting `WorktreeDeleted`. A forced removal (`--force`, e.g. uncommitted changes present) is deliberately **not** something an agent does unattended — it's in the project's permission "ask" list (`.claude/settings.json`) precisely because discarding uncommitted work should require a human look, even inside an agent-owned worktree.

If a task re-enters `Blocked` mid-execution and later resumes ([[004-Workflow]] §3), the existing worktree at the same path is reused, not recreated — the Execution Engine must check for an existing non-deleted `Worktree` row for that task before running step 2 again.

## 3. Sandboxing and Resource Limits

- **Tool permissions**: each agent activity's MCP tool access is scoped to what its role needs ([[005-Agents]] §8, [[009-MCP]]) — e.g. the Planner never gets write access to a worktree it doesn't have, the Deploy agent's DB migration tool is scoped to that Project's own database, not every database Forge knows about. Full detail belongs in [[014-Security]], not repeated here.
- **Resource limits**: standard Docker container CPU/memory limits per Worker, plus a Temporal `StartToCloseTimeout` per activity type (generous for Developer — this can legitimately run for hours per [[000-Vision]] §1 — much tighter for Deploy, where a hang likely means something is actually wrong rather than "still thinking").
- **Filesystem boundary**: a Worker's access is limited to its own home directory plus the specific Worktree path for the task it's currently running — never another task's worktree, even within the same Worker process.

## 4. Live Trace / Event Streaming (UC-9)

**Implemented, not just designed.** The actual mechanism ended up simpler than the two-channel design originally sketched here, and doesn't use Temporal Activity Heartbeats at all:

1. Every agent activity writes real `Event` rows to Postgres as it runs (`RecordEventAsync` in `AgentActivities`/`PersistenceActivities`, docs/003-Domain.md §4's catalog) — this was already true before live streaming existed, and remains the durable record.
2. Each write also issues a Postgres `NOTIFY task_events, '<task-id>'` (`PostgresNotify.TaskChangedAsync`, `Forge.Workflows`).
3. Forge.Api's `PostgresNotificationListener` (a `BackgroundService`) holds one dedicated `LISTEN task_events` connection, and on each notification calls `TaskEventBroadcaster.NotifyAsync(taskId)`.
4. `TaskEventBroadcaster` holds in-memory WebSocket connections per task ID (`/ws/tasks/{id}`, [[012-API]] §3) and pushes a minimal `"refresh"` message — no event data travels over the socket itself, the frontend re-fetches `GET /tasks/{id}` and `/tasks/{id}/events` on receiving it. Keeps the socket layer dumb; the REST endpoints stay the single source of truth for shape.

This decouples the Worker process (which has no idea the API or any frontend exists) from the API's WebSocket connections entirely through Postgres — no direct Worker→API call. **Validated live**: a raw WebSocket client connected before creating a task received `refresh` messages within milliseconds of each `Event` write, well ahead of the 10s fallback poll the frontend keeps as a safety net for a dropped socket.

**Real bug found and fixed during this validation**: the first implementation used EF Core's parameterized `ExecuteSqlAsync` for the `NOTIFY` call, which failed every single task workflow with `42601: syntax error at or near '$1'` — PostgreSQL's `NOTIFY` grammar only accepts the payload as a string literal, not a bind parameter, even through the extended query protocol. Fixed by using `ExecuteSqlRawAsync` with an explicit `#pragma warning disable EF1002` and a comment justifying why it's safe (the value is a typed `Guid`, not user input — no injection surface despite the analyzer's blanket warning).

Temporal Activity Heartbeats (for hang detection feeding [[006-Scheduler]] §3's retry policy) remain undesigned/unimplemented — a separate concern from the trace feed above, not yet needed since no activity has hung in practice.

## 5. Cost and Token Accounting

Every agent activity updates its `Run` row ([[003-Domain]]) as it consumes tokens — `prompt_tokens` / `completion_tokens` sourced from the Claude Agent SDK's own usage reporting, not estimated after the fact. `cost_estimate` is computed from the `Model.cost_per_token` metadata ([[008-ModelRouter]]) at write time, so historical `Run` rows keep the price that was actually in effect, even if pricing changes later.

## 6. Open Questions

- Temporal Activity Heartbeats for hang detection — not implemented; revisit if an activity actually hangs in practice rather than designing against a hypothetical.
- Exact activity timeout defaults per agent role beyond what's already in [[006-Scheduler]] §3's retry table — needs more real runs to tune, not guessed at further here.
- `TaskEventBroadcaster`'s in-memory connection registry only works for a single API process — fine at [[002-Architecture]] §4's current single-node scale, would need a real pub/sub (or rely on Postgres NOTIFY being received by every API instance, which it already is) once there's more than one Api replica.
