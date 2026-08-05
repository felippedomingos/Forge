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

Two distinct channels, not one, because they serve different purposes:

1. **Temporal Activity Heartbeats** — the agent activity calls `RecordHeartbeat` periodically with a small liveness payload. This is what lets Temporal detect a genuinely hung activity and apply its timeout/retry policy ([[006-Scheduler]] §3) — a liveness signal, not a verbose log (heartbeat payloads have a size limit and aren't meant for detailed streaming).
2. **Forge API trace endpoint** — the activity implementation also pushes each meaningful step (file read, command executed, a reasoning checkpoint) directly to the Forge API as it happens, which the API fans out over WebSocket to any frontend client with that task open. This is what actually renders as the "watch the agent work" view in [[000-Vision]] UC-9 and [[013-Frontend]] — a live append-only trace, not a poll against Temporal's history API (which is optimized for workflow replay, not for a smooth UI feed).

Both channels are backed by the same underlying `Run` entity ([[003-Domain]]) — heartbeats keep Temporal's own view of liveness current, while the trace endpoint's entries are what a human actually reads.

## 5. Cost and Token Accounting

Every agent activity updates its `Run` row ([[003-Domain]]) as it consumes tokens — `prompt_tokens` / `completion_tokens` sourced from the Claude Agent SDK's own usage reporting, not estimated after the fact. `cost_estimate` is computed from the `Model.cost_per_token` metadata ([[008-ModelRouter]]) at write time, so historical `Run` rows keep the price that was actually in effect, even if pricing changes later.

## 6. Open Questions

- Whether the Forge API trace endpoint should also persist to the `Event` table (making it queryable/replayable after the fact) or is purely a live/ephemeral WebSocket feed — leaning towards persisting it, since [[000-Vision]] §5 commits to a complete audit trail, but not decided here.
- Exact heartbeat interval and activity timeout defaults per agent role — needs real numbers once a Developer agent actually runs against a project, not guessed at in the abstract.
