# 003 — Domain Model

## Status

Draft — Phase 2 (Domain)

## 1. Entities

| Entity | Key fields | Notes |
|---|---|---|
| **Project** | `id`, `name`, `prefix`, `next_task_number`, `repository_url`, `root_branch` (`main`/`develop`/`dev`), `git_provider_plugin_id`, `local_path` (nullable), `publish_recipe` (nullable JSON), `allow_agent_bypass_permissions` (bool, default `false`), `created_at` | Maps 1:1 to a Git repository (see [[000-Vision]] §7). `git_provider_plugin_id` points at a [[010-Plugins]] instance — GitHub for every project at v1 per [[ADR-0002]]. `prefix` (e.g. `"FORGE"`, uppercase, unique) + `next_task_number` build every Task's human-readable tag — founder-requested, since a raw GUID isn't something anyone can reference in conversation. `prefix` is immutable once a Task references it. `allow_agent_bypass_permissions` gates whether the Developer/Deploy agents may edit files or run shell commands for this project's tasks at all ([[009-MCP]] §4, [[005-Agents]] §4/§5) — a project must be explicitly marked trusted before either role can do anything destructive. |
| **Task** | `id`, `project_id`, `number`, `title`, `description` (nullable until planned), `state`, `priority` (nullable until prioritized), `branch_name` (nullable until execution), `worktree_id` (nullable), `created_at`, `updated_at` | The aggregate root. Owns its `AcceptanceCriterion` list, its `SubTask`s, and is the subject of every `Event` scoped to it. `number` is sequential per-project (assigned atomically from `Project.next_task_number` on creation — [[012-API]] `POST /tasks`), combined with the owning Project's `prefix` to form the task's tag (`"FORGE-42"`), unique per `(project_id, number)`. |
| **SubTask** | `id`, `task_id`, `title`, `description`, `order`, `done` (bool) | A planning artifact created by the Planner agent, not a first-class state-machine participant — see §3 for how it relates to its parent Task's lifecycle. |
| **AcceptanceCriterion** | `id`, `task_id`, `description`, `satisfied` (bool) | Modeled as its own entity (not a text blob) so the UI can render/check them individually and each can carry its own audit trail. |
| **Worker** | `id`, `name`, `status` (`idle`/`busy`/`offline`), `current_task_id` (nullable), `home_directory`, `created_at` | An isolated execution environment ([[000-Vision]] §7). |
| **Worktree** | `id`, `task_id`, `project_id`, `path`, `branch_name`, `created_at`, `deleted_at` (nullable) | At most one *active* (non-deleted) Worktree per Task — see invariant in §2. |
| **Run** | `id`, `task_id`, `agent_role` (`Planner`/`Prioritizer`/`Developer`/`Deploy`/`Git`), `model_provider`, `started_at`, `finished_at`, `status` (`success`/`failed`/`timeout`), `prompt_tokens`, `completion_tokens`, `cost_estimate`, `claude_account_id` (nullable) | One row per agent invocation. Feeds the cost/observability requirements in [[000-Vision]] UC-9. `claude_account_id` (added 2026-08-08, [[adr/ADR-0005]]) records which `ClaudeAccount` handled this invocation, if any were configured - null under the zero-config default. |
| **Event** | `id`, `task_id` (nullable — some are system-level), `type`, `payload` (JSON), `occurred_at`, `actor` (`user:<id>` or `agent:<role>`) | Immutable. See the catalog in §4. This is the audit trail from [[000-Vision]] §5, largely backed by Temporal's own workflow history per [[ADR-0001]]. |
| **Plugin** | `id`, `name`, `kind` (`git_provider`/`issue_tracker`/`cloud_cli`/`database`/`deployment_target`), `version`, `configuration` (JSON) | See [[010-Plugins]]. |
| **Model** | `id`, `provider`, `capability_tier`, `cost_per_token`, `enabled` | Claude-only row populated at v1 per [[ADR-0003]]; schema supports more from day one. |
| **User** | `id`, `name`, `email`, `role` | Maps to the personas in [[000-Vision]] §6. |
| **ClaudeAccount** | `id`, `name`, `user_id`, `token`, `is_active`, `created_at` | Founder-requested (2026-08-08, [[adr/ADR-0005]]) - multi-account Claude failover with real usage tracking, linked to the `User` who manages it. `token` is a long-lived credential from `claude setup-token`, plaintext in Postgres (same posture as `Project.git_credential`), never returned over HTTP. See [[012-API]] for the endpoints and [[005-Agents]]/`ClaudeAccountPool.cs` for how it's used. |
| **AgentMemory** | `id`, `project_id`, `agent_role`, `key`, `value`, `updated_at` | Project-wide **shared** memory, not per-role despite the `agent_role` column ([[005-Agents]] §7 explains the reconciliation) — every agent role reads every entry for the project regardless of who wrote it. Unique on `(project_id, agent_role, key)`; the API ([[012-API]]) ignores `agent_role` entirely and always writes `Planner` as a stable default. |

## 2. Aggregates and Invariants

- **Task is the aggregate root** for its `SubTask`s, `AcceptanceCriterion`s, and the `Event`s scoped to it.
- **INV-1**: a Task belongs to exactly one Project (UC-2). Never nullable, never reassignable after creation.
- **INV-2**: a Task has at most one *active* Worktree at a time. Enforced structurally by modeling Task lifecycle as one Temporal workflow execution per [[ADR-0001]] — a second worktree can't be created while the first is live because the workflow itself is single-threaded per task.
- **INV-3**: a Task's state can only change via a valid edge in the state machine in §3 below. This is enforced by the Temporal workflow definition, not merely validated in an application-layer service — an illegal transition should be structurally unrepresentable, not just rejected by a check.
- **INV-4**: `SubTask.done` can only be set to `true` once its parent Task has reached `Executing` or later. A sub-task is a planning artifact before execution begins; marking it "done" earlier has no meaning. (Flagged as an assumption — confirm when [[005-Agents]] specifies exactly how the Developer agent consumes sub-tasks.)

## 3. Task State Machine

States (unchanged from [[000-Vision]] §9): `Inbox`, `Backlog`, `Blocked`, `Todo`, `Executing`, `AwaitingPublish`, `Publishing`, `Review`, `Done`, `Production`.

### Reconciling the founder's original spec: how many agents move a task into `Todo`?

The original scope described three actors between `Backlog` and `Todo`: a Planner, a Prioritizer, and "a third agent that moves the task to Todo." [[000-Vision]] UC-7 already softened the third step to "a system... moves it to Todo" rather than naming a distinct LLM agent. This document makes that explicit as a modeling decision: promoting a task from `Backlog` to `Todo` is **deterministic scheduler logic** ([[006-Scheduler]]) — it fires when a worker slot is free and the task is at the top of the prioritized backlog — not an LLM judgment call. Only `Planner`, `Prioritizer`, `Developer`, `Deploy` and `Git` are LLM-driven agent roles (see [[005-Agents]]). This keeps the state machine's actor column honest: some transitions are triggered by an agent's output, some by a human moving a card, and some by plain scheduling logic.

### Valid transitions (happy path)

| # | From | To | Trigger event | Actor | Guard |
|---|---|---|---|---|---|
| 1 | `Inbox` | `Backlog` | `PlannerCompleted` | Planner agent | Description + acceptance criteria produced |
| 2 | `Inbox` | `Blocked` | `PlannerNeedsClarification` | Planner agent | Planner recorded at least one open question |
| 3 | `Blocked` | `Inbox` | `UserAnsweredQuestions` | User | At least one answer provided (UC-5) |
| 4 | `Backlog` | `Todo` | `TaskPromotedToTodo` | Scheduler (deterministic, see reconciliation above) | Task is prioritized; a worker slot is free |
| 5 | `Todo` | `Executing` | `WorkerAllocated` | Scheduler / Developer agent | Root branch sync succeeds; worktree created |
| 6 | `Executing` | `AwaitingPublish` | `DeveloperCompleted` | Developer agent | Build and tests pass |
| 7 | `AwaitingPublish` | `Publishing` | `UserRequestedPublish` | User (moves card to Publish) | None beyond the human decision (UC-10) |
| 8 | `Publishing` | `Review` | `DeployCompleted` | Deploy agent | Publish steps (code, DB, other local changes) succeeded |
| 9 | `Review` | `Done` | `UserApprovedReview` | User | None beyond the human decision |
| 10 | `Done` | `Production` | `PipelineConfirmedDeployment` | External CI/CD (webhook) or `TaskWorkflow`'s own PR-merge polling ([[015-Deployment]] §4) | Pipeline reports success, or the task's own `PullRequestUrl` is detected merged |

### Not yet specified (deferred to [[004-Workflow]])

The happy path above is fully determined by the founder's original spec. What's **not** yet decided, and is explicitly out of scope for this document:

- Failure edges: what happens when `DeveloperCompleted` should instead be `DeveloperFailed` (does the task return to `Todo`? to a new `Blocked` variant?), and equivalently for `DeployFailed`.
- Timeout behavior for `Blocked` (open question already flagged in [[000-Vision]] §12).
- Whether `Publishing → Review` can roll back if a deploy partially fails (migrations applied, restart failed).

[[004-Workflow]] owns the full transition table including these failure/rollback/timeout paths; this document only establishes the states, the happy-path edges, and the invariant that no other edge is legal without an explicit decision recorded there.

## 4. Event Catalog (initial)

Each row is an `Event.type` value. This list will grow; it is not meant to be exhaustive on first pass.

`TaskCreated`, `PlannerStarted`, `PlannerInvokingModel`, `PlannerCompleted`, `PlannerNeedsClarification`, `UserAnsweredQuestions`, `PrioritizationCompleted`, `TaskPromotedToTodo`, `WorkerAllocated`, `DeveloperStarted`, `DeveloperCompleted`, `DeveloperFailed`, `MemoryStrengthened`, `UserRequestedPublish`, `DeployStarted`, `DeployMigrationCompleted`, `DeployRestartCompleted`, `DeployHealthCheckPassed`, `DeployCompleted`, `DeployFailed`, `DeployConflictDetected`, `DeployConflictVerified`, `UserApprovedReview`, `ReviewRequestedChanges`, `GitCommitted`, `GitPushed`, `PRCreated`, `GitBranchAlreadyIntegrated`, `WorktreeDeleted`, `PipelineConfirmedDeployment`, `AutoRecovered`, `SchedulerAutoRecovered`.

`DeployMigrationCompleted`/`DeployRestartCompleted`/`DeployHealthCheckPassed` were added once `DeployAsync` actually ran all three `PublishRecipe` steps ([[015-Deployment]] §2-3) instead of just `migrationCommand` — each step gets its own event so the timeline shows which part of the recipe succeeded, not just a single opaque `DeployCompleted`. `ReviewRequestedChanges` backs row 14 ([[004-Workflow]] §3a) — carries the reviewer's comment as payload, read by the next `DevelopAsync` run. `AutoRecovered` ([[006-Scheduler]] §4a) is written by the scheduler's own stuck-task safeguard, not an agent - `Actor` is `system:auto-recovery` rather than an `agent:`/`user:` prefix, and the payload carries the prior Temporal status and which of the two recovery tiers fired. `SchedulerAutoRecovered` ([[006-Scheduler]] §4b) is its global-watchdog sibling - `TaskId` is null (system-level, not scoped to one task) and `Actor` is `system:global-watchdog`; payload carries the restarted project's id. `DeployConflictDetected`/`DeployConflictVerified` ([[015-Deployment]] §3a) bracket the AI conflict-resolution attempt on Deploy's `LocalPath` merge - the resolution itself still ends in the existing `DeployBranchMerged`/`DeployFailed`, now carrying an `aiResolved: true` flag on success. `MemoryStrengthened` ([[005-Agents]] §7) is written whenever `DevelopAsync`'s own model response includes a non-empty `memoryKey`/`memoryNote` - `Actor` is `agent:Developer` like any other Developer event, payload carries the key/note that was upserted into `AgentMemory`.

`PlannerInvokingModel` was added once the Planner agent became real ([[ADR-0005]]) - it's the one event type that exists purely to give the task detail view ([[013-Frontend]]) something to show while a real LLM call is in flight, rather than a silent gap between `PlannerStarted` and `PlannerCompleted`.

`TaskMoved` is kept as a generic umbrella type for any card move captured by the UI that doesn't yet map to one of the named events above (e.g. a manual correction) — every legitimate transition should eventually be one of the named events, not this umbrella, per INV-3.

## 5. Domain Services vs. Agent Responsibilities

An LLM agent **proposes** a transition (e.g. the Planner agent decides a task is unclear and would emit `PlannerNeedsClarification`); the domain layer (the Temporal workflow per [[ADR-0001]]) **disposes** — it only accepts the event if it corresponds to a legal edge in §3 for the task's current state. An agent's judgment is never itself the authority on whether a transition is valid; the state machine is.

## 6. Open Questions

Carried forward for [[004-Workflow]] and [[005-Agents]]:

- Exact semantics of `SubTask` vis-à-vis the parent Task's own state — does a task with incomplete sub-tasks block the `Executing → AwaitingPublish` transition, or is that left to the Developer agent's judgment?
- Whether `Run` should record partial/incremental agent progress (useful for the live trace in UC-9) or only start/finish — likely resolved in [[007-ExecutionEngine]].
