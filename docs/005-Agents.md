# 005 — Agents

## Status

Draft — Phase 3 (Architecture)

## 1. Common Shape

Every agent role below runs as a Temporal Activity ([[ADR-0001]]), invoked by exactly one workflow transition ([[003-Domain]] / [[004-Workflow]]), and produces exactly one domain event on completion (success or a documented failure edge). None of the five roles spans more than one lifecycle stage — this is the "an agent owns exactly one state transition" boundary from [[000-Vision]] §5.

All five roles run against a single provider at v1 (Claude, per [[ADR-0003]]) through the Model Router abstraction in [[008-ModelRouter]].

## 2. Planner Agent

- **Trigger**: Task enters `Inbox` (new, title-only or with founder-requested seed notes, or re-entered via `UserAnsweredQuestions`).
- **Inputs**: `Task.title`, `Task.description` (empty on first run **unless** the human creating the task provided seed notes — [[012-API]] `POST /tasks`; may contain a URL to a spec/issue/design), prior Q&A history (from `Event` rows, when re-entering from `Blocked`), the Project's repository at its root branch, and this project's shared project memory (§7).
- **Tools**: read-only repository access (no worktree — the Planner never writes code, so it doesn't need [[003-Domain]]'s per-task Worktree; a shared read-only clone of the root branch per project is sufficient), **`WebFetch` (implemented)** - pre-approved via `ClaudeCliProvider`'s `--allowedTools WebFetch`, not the blanket `bypassPermissions` the Developer needs, since the Planner stays read-only otherwise; explicitly instructed to fetch any URL found in the seed notes rather than guessing from link text alone. `WebSearch` and the Project's issue tracker (GitHub Issues at v1 per [[ADR-0002]]) remain unimplemented (Azure DevOps / `az cli` access from the founder's original scope is explicitly deferred with that ADR, not silently dropped).
- **Outputs**: `Task.description`, `AcceptanceCriterion` rows, optionally `SubTask` rows, then either `PlannerCompleted` (→ `Backlog`) or `PlannerNeedsClarification` (→ `Blocked`) with the open questions recorded as event payload.
- **Failure handling**: transient tool/API failures retry per Temporal's activity policy without a domain-visible transition ([[004-Workflow]] §4); genuine inability to resolve the task always produces `PlannerNeedsClarification`, never a guess.

## 3. Prioritizer Agent

- **Trigger**: a task enters `Backlog`, or an existing `Backlog` task's description/criteria changed after re-planning.
- **Scope decision**: prioritization is **per-project**, not global across all of a user's projects. [[000-Vision]] UC-6 says a task is ordered "against everything else in the backlog" without specifying scope; comparing effort/value across unrelated codebases is a materially harder problem than ranking within one project's backlog, so it's out of scope for v1 and tracked in [[016-Roadmap]] instead of guessed at here.
- **Inputs**: every `Backlog` task for that project — title, description, acceptance criteria, creation order (tiebreaker).
- **Tools**: read-only access to the Project's backlog through the Forge API's internal service layer (agents never query Postgres directly — see [[012-API]]).
- **Outputs**: `Task.priority` for every affected task, `PrioritizationCompleted` event per task.
- **Failure handling**: no `Blocked` path exists for this role — only the Planner can block a task ([[003-Domain]] §3). If prioritization fails repeatedly, the task simply remains un-prioritized and visible as such in the UI; no other domain-visible failure state is introduced at v1.

## 4. Developer Agent

- **Trigger**: `WorkerAllocated` — the task was promoted to `Todo` and a Worker slot is free ([[003-Domain]] row 5).
- **Inputs**: `Task.description`, acceptance criteria, sub-tasks, `Project.repository_url` / `root_branch`, shared project memory (§7).
- **Tools**: Git worktree/branch operations via the GitHub plugin ([[ADR-0002]]), filesystem read/write scoped to its Worktree, terminal (build/test), and any additional MCP servers a project's stack requires. **Gated on `Project.AllowAgentBypassPermissions`** ([[003-Domain]] §1, [[009-MCP]] §4) — refuses outright (`DeveloperNeedsClarification` → `Blocked`, explaining why) for any project not explicitly marked trusted, since editing files at all in a headless subprocess requires Claude Code CLI's full permission bypass (no human present to click "allow" - [[adr/ADR-0005]]).
- **Clarifying the founder's original spec — when does the Developer agent commit?** The original flow only mentions git commit/push/PR at the final `Done` step (owned by the Git agent, §5). Read literally, that would mean zero git history exists until after human review, which is bad practice for anything but a trivial change. **Decision**: the Developer agent commits to its local branch inside the worktree as it works, for its own checkpointing and to leave a real history — these commits stay local (not pushed) until the Git agent's stage. The Git agent (§5) owns *pushing* that branch and opening the PR, not the act of committing itself. This preserves the founder's intent (nothing reaches the remote/PR stage before `Done`) while not requiring an agent to hold hours of uncommitted work in a worktree.
- **Outputs**: `DeveloperCompleted` (→ `AwaitingPublish`, build/tests pass) or `DeveloperNeedsClarification` (→ `Blocked`, per [[004-Workflow]] §3 — re-entry always goes through `Inbox`, resuming against the *same* worktree rather than recreating it).
- **Live trace**: every meaningful step (file read, command run, reasoning checkpoint) is surfaced incrementally, not just at completion — see [[000-Vision]] UC-9 and [[007-ExecutionEngine]] for the streaming mechanism.
- **`git fetch`/`git worktree add` failing goes straight to `DeveloperNeedsClarification` → `Blocked`, not a thrown exception (found live, 2026-08-07).** Throwing let Temporal's activity retry policy treat it as transient (5 attempts, growing backoff) - fine for a flaky connection, actively harmful for a persistent failure like an expired PAT/SSH key, which will never recover by retrying. Once those retries exhausted, the exception propagated all the way up through `TaskWorkflow.RunAsync` uncaught, crashing the whole workflow - the stuck-task safeguard ([[006-Scheduler]] §4a) would then "recover" it by starting a fresh execution, re-running the Planner (a real, paid LLM call) from scratch, only to hit the identical git failure and repeat the cycle. Confirmed live: two tasks looped like this for over an hour before being noticed. Treated identically to the untrusted-project/missing-`LocalPath` checks earlier in the same activity - immediately `Blocked` with the git stderr as the reason, no retry, no crash.

## 5. Deploy Agent

- **Trigger**: `UserRequestedPublish` — the human moved the task from `AwaitingPublish` to `Publish`.
- **Inputs**: the Task's worktree/branch, and the Project's `PublishRecipe` ([[015-Deployment]] §2-3 — resolved, not a gap anymore: `migrationCommand`, `restartTargets` via `docker compose restart`, and `healthCheckUrl` polling, in that order).
- **Tools**: terminal, DB migration tooling, Docker (restart/rebuild), health-check calls. **Gated on `Project.AllowAgentBypassPermissions`**, same reasoning as the Developer agent (§4) — `migrationCommand`/`restartTargets` are arbitrary shell execution, refused outright for an untrusted project.
- **Outputs**: `DeployCompleted` (→ `Review`) or `DeployFailed` (→ `AwaitingPublish`, per [[004-Workflow]] §5 — no auto-retry, human re-triggers after inspecting).

## 6. Git Agent

- **Trigger**: `UserApprovedReview` — the human moved the task from `Review` to `Done`.
- **Inputs**: the Task's worktree/branch, already containing the Developer agent's local commits (§4).
- **Tools**: the GitHub plugin ([[ADR-0002]]) for push and PR creation.
- **Outputs**: `GitPushed`, `PRCreated`, then `WorktreeDeleted` once cleanup completes. The task then waits for the external `PipelineConfirmedDeployment` event ([[003-Domain]] row 10) to reach `Production` — the Git agent does not itself decide when that happens.

## 7. Per-Project Shared Memory

**Implemented.** Modeled as a simple key/value store, `AgentMemory` ([[003-Domain]] §1, [[011-Database]]), not a vector/embedding store — retrieval is "load everything recorded for this project" rather than semantic search, since per-project memory is expected to stay small (tens of entries, not thousands).

**Reconciling the schema with how it's actually used**: the table is keyed `(project_id, agent_role, key)` — scoped per-role, as originally conceived (each agent role accumulating its own notes, e.g. "this project uses XAF"). In practice, the founder's request was for **project-wide shared** memory: one place to record conventions/decisions/gotchas that every agent should know about a project, not siloed by which role wrote it. `AgentActivities.FormatMemoryAsync` reflects this — it loads every entry for the project regardless of `agent_role`. The `agent_role` column still exists (the API always writes `Planner` as a stable default when creating an entry, [[012-API]]), but nothing reads it as a filter — it's a vestige of the original per-role design, not a boundary anyone relies on.

Editable directly per-project via the frontend's project sidebar → edit dialog ([[013-Frontend]]), not just written by agents — the founder can seed memory (or correct/delete an agent's note) without a database client.

**Standing policy (founder-requested, 2026-08-07): every LLM call made on behalf of a project's tasks reads that project's shared memory first**, no exceptions per-role — this was previously true only for Planner (`PlanAsync`) and Developer (`DevelopAsync`); now also `PrioritizeAsync` (a ranking call like "always ship compliance-flagged work first" is exactly the kind of standing guidance memory exists for) and Deploy's AI merge-conflict resolution (`TryResolveMergeConflictAsync`, [[015-Deployment]] §3a — the one place Deploy itself invokes an LLM; the rest of `DeployAsync` is deterministic `PublishRecipe` execution with nothing for an LLM to read memory into). `GitFinalizeAsync` and the rest of `DeployAsync` remain memory-free for the same reason: they never call an LLM, so there's no prompt to splice anything into — this is a boundary of "no LLM call happens here," not a gap in the policy. When a sixth agent role or a new LLM-invoking activity is added, splicing in `FormatMemoryAsync`'s output is not optional — it's the default, to be skipped only with a documented reason (e.g. a call that's deliberately memory-blind for isolation/testing).

**Memory is also strengthened automatically, not just read (founder-requested, 2026-08-07): "sempre que terminar algo, fortalecer a memoria do projeto."** `DevelopAsync`'s own prompt asks the model for an optional `memoryKey`/`memoryNote` alongside its usual response — a short kebab-case key and a one-to-three-sentence note, only when the task revealed something about *this specific* codebase genuinely worth a future task knowing (a non-obvious convention, a gotcha, a decision and its reasoning), explicitly told to leave both null rather than forcing a note most tasks don't warrant. When both are present, `DevelopAsync` upserts it into `AgentMemory` by key (same semantics as `PUT /projects/{id}/memory` — a later task correcting/refining an earlier note overwrites it rather than accumulating duplicates) and records a `MemoryStrengthened` event ([[003-Domain]]). Deliberately scoped to `DevelopAsync` alone for now, not every role — it's the one point in the lifecycle where an agent is actually reading/editing real files in the repository, and so the one most likely to discover something concretely worth remembering; extend to Planner/Deploy only if a real case shows up where they do too, not preemptively.

## 8. Agent-to-Tool Contract

All five roles reach external systems exclusively through MCP servers ([[009-MCP]]) — no agent role has a bespoke, hand-rolled integration to Git, a cloud CLI, or a database. A tool call failure is retried per the same Temporal activity policy as any other transient failure ([[004-Workflow]] §4); a tool being unavailable/misconfigured (not merely slow or rate-limited) should surface as the same "genuine failure" path each role already has (`PlannerNeedsClarification`, `DeveloperNeedsClarification`, `DeployFailed`) rather than a silent retry loop with no visible end state.
