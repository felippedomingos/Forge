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
| **Per-worker** | Temporal's own `maxConcurrentActivityExecutionSize` on the Worker process | Bounds how many agent activities one Worker process runs in parallel — a resource ceiling (CPU/memory), not a domain concept. |
| **Global** | Total concurrent LLM calls across all projects | Respects the single provider's rate limits ([[ADR-0003]] — Claude only at v1, so this is one shared ceiling, not per-provider accounting yet). |

When a cap is hit, tasks simply wait at their current state (`Backlog` unpromoted, or a `Run` queued but not yet started) — this is normal backpressure, not a failure, and should be visible in the UI as "waiting for a slot," not silently invisible.

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

## 5. Open Questions

- Should per-project concurrency limits be a static config value or dynamically tunable from the UI? Leaning towards a simple per-Project setting exposed in [[013-Frontend]], not invented further here.
- Whether `BacklogSchedulerWorkflow` needs its own crash-recovery story beyond what Temporal already gives every workflow — likely not, but worth confirming once [[007-ExecutionEngine]] is implemented against a real Worker process.
