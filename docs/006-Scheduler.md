# 006 — Scheduler

## Status

Draft — Phase 3 (Architecture)

## 1. What the Scheduler Actually Is

There is no separate scheduler service in this architecture. Given Temporal already owns workflow execution ([[ADR-0001]]), the cleanest choice is to implement scheduling logic *as* a Temporal workflow rather than bolt an external scheduler component onto the side — one more moving part with its own failure mode would be exactly the kind of avoidable complexity [[000-Vision]] §5 warns against.

**Decision**: one long-running `BacklogSchedulerWorkflow` per Project. It watches two things — the set of `Backlog` tasks ordered by `Task.priority`, and Worker slot availability for that project — and is responsible for the `TaskPromotedToTodo` event ([[003-Domain]] row 4) and the `WorkerAllocated` event (row 5). It never touches a task once it's in `Todo` or later; from that point the task's own per-task workflow takes over.

This reuses Temporal's own primitives (signals for "a worker freed up", "a new task entered Backlog", "priorities changed") instead of a bespoke polling loop — consistent with [[002-Architecture]] §2's stance that event-driven orchestration is what Temporal provides natively, not a layer built on top of it.

**What's actually implemented today is a fixed 5-second poll loop**, not the event-driven design above — a documented, deliberate stand-in ([[BacklogSchedulerWorkflow]]'s own "KNOWN SIMPLIFICATION" comment), not yet the target design. **Found live, and now fixed**: a real long-lived project's scheduler ran for ~19 hours at that interval and hit Temporal's history size limit (51,200 events), which the server responded to by terminating the workflow outright - `docs/016-Roadmap.md`'s "no `ContinueAsNewAsync` yet" caveat wasn't hypothetical. Fixed by checking `Workflow.ContinueAsNewSuggested` once per loop iteration and continuing-as-new when the server recommends it, rather than guessing at a fixed iteration count that would be right for one poll interval and wrong for another. `POST /projects/{id}/resume-scheduler` ([[012-API]]) exists to recover a scheduler that terminates anyway (this fix, a bug, or anything else) without losing the project's scheduling state - the same recovery shape as `POST /tasks/{id}/resume`.

## 2. Concurrency Limits

Three independent caps, all configurable per Project/globally, not hardcoded:

| Level | What it limits | Why |
|---|---|---|
| **Per-project** | How many tasks from one Project can be `Executing` simultaneously | A project's local dev environment (its own Postgres/services, per [[015-Deployment]]) may not tolerate two Developer agents mutating shared local state at once, even though [[003-Domain]] INV-2 already isolates them at the git-worktree level. |
| **Per-worker** | Temporal's own `maxConcurrentActivityExecutionSize` on the Worker process | Bounds how many agent activities one Worker process runs in parallel — a resource ceiling (CPU/memory), not a domain concept. **Still not implemented** - only one Worker process exists today and nothing has yet contended for this. |
| **Global** | Total concurrent `Executing` tasks across every project combined | **Implemented (2026-08-08), founder-requested** - `SystemSettings.MaxGlobalConcurrentExecuting` (default 6, editable Admin-only via the sidebar's Settings dialog / `PATCH /settings`), enforced in the same atomic check as the per-project cap. Closes [[001-Requirements]] NFR-1's last open gap. |

When a cap is hit, tasks simply wait at their current state (`Backlog` unpromoted, or a `Run` queued but not yet started) — this is normal backpressure, not a failure, and should be visible in the UI as "waiting for a slot," not silently invisible.

**The per-project cap's enforcement had a real race, found live (2026-08-07).** `TaskWorkflow.cs`'s Todo→Executing gate (`SchedulingActivities.HasExecutingCapacityAsync`) used to be a bare read-only check — count current `Executing` rows, return `count < max`. Two Todo tasks for the same project calling this at nearly the same instant, with exactly one slot free, could both read the identical pre-write count and both get `true`: the actual `Executing`-state write happens moments *later*, in a separate activity call, well outside this check. Confirmed live: two `DeveloperStarted` events 17ms apart in the same project, which then sat at 3 `Executing` tasks against a configured limit of 2. Rechecking more often wouldn't have helped — only claiming the slot atomically, in the same transaction as the check, does. Fixed by wrapping the check in a per-project Postgres advisory transaction lock (`pg_advisory_xact_lock(hashtext(projectId))`, serializing concurrent callers for the *same* project only — a different project's callers proceed independently) and writing the `Executing` state itself inside that same locked transaction, before releasing it — the second caller's count query then genuinely runs after the first has already committed its claim.

**The global cap reuses this exact mechanism, one day later, with one change: the lock key is now fixed (`pg_advisory_xact_lock(0)`), not per-project.** A fixed key serializes *every* caller globally, which is a strict superset of "serialize same-project callers" - so the same lock now atomically checks and claims against both the per-project and the global ceiling in one pass, rather than needing two different lock granularities. Promotion isn't a hot path (once per task transition, not per request), so full global serialization here is a deliberate, cheap trade-off, not a scaling concern.

## 3. Retry and Backoff Policy

Default Temporal activity retry policy (tunable per activity type, not a single global constant):

| Agent activity | Initial interval | Backoff coefficient | Max interval | Max attempts | Why |
|---|---|---|---|---|---|
| Planner, Prioritizer | 30s | 2.0 | 10m | 5 | Read-heavy, safe to retry aggressively — no side effects to compound. |
| Developer | 30s | 2.0 | 10m | 5 | Same reasoning; a retried Developer activity resumes against the same worktree ([[004-Workflow]] §3), so retries are safe. |
| Deploy | 30s | 2.0 | 5m | **2** | Deliberately fewer attempts — a deploy activity has real side effects (migrations, restarts); retrying a partially-applied deploy blindly risks compounding the problem. After 2 attempts, the activity is marked failed and the domain-level `DeployFailed` edge ([[004-Workflow]] §5) takes over — no further automatic retry, a human re-triggers `Publish`. |
| Git | 30s | 2.0 | 10m | 5 | Push/PR creation is safe to retry (idempotent from Git's perspective — pushing the same commits twice is a no-op). |

This table only governs *transient* failures per [[004-Workflow]] §4 — once an activity's own logic decides a failure is not transient (a genuine ambiguity, a real deploy error), it emits the domain event directly rather than exhausting these retries first.

## 4. Priority Handling

Temporal task queues are FIFO by default — they have no native concept of the `Task.priority` set by the Prioritizer agent ([[003-Domain]], [[005-Agents]] §3). Priority ordering is therefore implemented as application logic inside `BacklogSchedulerWorkflow`: whenever a Worker slot frees up for a Project, the workflow queries that Project's `Backlog` tasks ordered by `Task.priority` (creation order as tiebreaker, per [[005-Agents]] §3) and promotes the top one — priority is a query the scheduler makes, not a property the underlying queue understands.

## 4a. Auto-Recovery Safeguard (found necessary live, 2026-08-06)

This session hit three separate real incidents where a `TaskWorkflow` died outright (a missing activity registration on the Worker, twice) or got wedged in an infinite non-determinism retry loop (`TMPRL1100`, from a code change landing while a real task's history predated it) — in every case, the affected task just sat frozen in whatever board state it was in, with nothing visible anywhere that something was wrong short of cross-checking Temporal directly.

`BacklogSchedulerWorkflow` now calls `SchedulingActivities.RecoverStuckTasksAsync(projectId)` every 5 minutes (`Workflow.Patched`-guarded, since both of this session's scheduler executions predate this code). It covers two cases, both recovering via the same mechanism `POST /tasks/{id}/resume` uses — a fresh `TaskWorkflow` execution with `resumeFrom` set to the task's own last-persisted `State`, so nothing already-completed gets redone:

1. **Workflow already terminal** (`Failed`/`Terminated`/`TimedOut`/`Canceled`) — recovered immediately, regardless of how long ago that happened.
2. **Workflow still `Running` but stuck** — only for a task that hasn't moved in 15 minutes *and* is in an agent-driven state (`Inbox`/`Backlog`/`Todo`/`Executing` — nothing here should ever be waiting on a human) *and* whose workflow's own most recent history event is itself a task failure (the concrete symptom of a non-determinism loop). Terminated, then resumed.

`Blocked`/`AwaitingPublish`/`Publishing`/`Review` are never touched by the staleness check — those legitimately wait on a human for as long as it takes, and "hasn't moved in 15 minutes" is completely normal there, not a bug. Every recovery is recorded as an `AutoRecovered` event ([[003-Domain]]) with the prior status and reason, so it's auditable from the task's own timeline.

## 4b. Global Watchdog (found necessary live, 2026-08-07)

§4a's safeguard has a single point of failure: it only ever runs from *inside* a project's own `BacklogSchedulerWorkflow`, so its uptime is entirely dependent on that one workflow's uptime. Found live: 7 SlayZone-imported project schedulers ([[016-Roadmap]]) had been deliberately terminated earlier in the same session (to stop a real over-promotion incident) and never restarted — their §4a safeguard silently stopped running with them, and two tasks in one of those projects sat genuinely stuck (their `TaskWorkflow`s had actually `Failed`) for 2.5+ hours with zero recovery attempted, the exact failure class §4a exists to prevent.

`GlobalWatchdogWorkflow` — one single global instance (fixed workflow ID `"global-watchdog"`), started once from `Forge.Api`'s own startup (idempotent: a second start attempt just hits Temporal's default `WorkflowIdReusePolicy` refusing to double-start over an open execution, silently caught) — runs independent of any project's own scheduler, every 5 minutes:

1. **Restarts any project's scheduler that's dead** (`GlobalWatchdogActivities.EnsureSchedulersRunningAsync`) — the actual root cause fix: a dead scheduler also stops backlog promotion/prioritization for that project, not just stuck-task recovery. Recorded as a `SchedulerAutoRecovered` event ([[003-Domain]]), system-level (`TaskId` null, `Actor` `system:global-watchdog`).
2. **Calls `RecoverStuckTasksAsync` directly for every project**, regardless of whether its scheduler is currently healthy — genuine defense in depth: this alone would have caught the incident above even without restarting anything, and covers a scheduler whose top-level Temporal status is `Running` but is internally wedged for some other reason §4a's own mechanism wouldn't otherwise reach.

Deliberately a *separate* workflow rather than folding into §4a's per-project loop — a global concern (project schedulers, plural) shouldn't live inside any one project's own workflow, or fixing one project's scheduler would depend on a *different* project's scheduler happening to still be alive.

## 5. Open Questions

- Should per-project concurrency limits be a static config value or dynamically tunable from the UI? Leaning towards a simple per-Project setting exposed in [[013-Frontend]], not invented further here.
- The auto-recovery safeguard (§4a) checks every non-terminal task on a fixed 5-minute timer, same known limitation as the rest of this document (event-driven would be the target design, not built yet). At today's scale (a handful of tasks per project) a `DescribeAsync` + occasional history fetch per task every 5 minutes is negligible load; would need revisiting if a project ever has hundreds of concurrently in-flight tasks.
- §4a's tier-2 (stuck-but-`Running`) detection is a heuristic (last history event is a task failure) tuned to the exact failure mode found live this session - it wouldn't catch every conceivable way a workflow could silently stop making progress, just the one actually observed.
- Whether `BacklogSchedulerWorkflow` needs its own crash-recovery story beyond what Temporal already gives every workflow — likely not, but worth confirming once [[007-ExecutionEngine]] is implemented against a real Worker process.
