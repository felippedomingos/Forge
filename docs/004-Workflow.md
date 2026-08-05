# 004 — Workflow

## Status

Draft — Phase 2 (Domain)

## 1. Scope

[[003-Domain]] §3 established the states and the happy-path transitions. This document adds everything the founder's original spec left implicit: failure edges, timeout/escalation rules, rollback semantics, and how a Task relates to its SubTasks operationally. Every decision below is a deliberate, documented choice — not a guess — so it can be revisited with full context rather than rediscovered from code.

## 2. Full Transition Table

Extends [[003-Domain]] §3's happy-path table (rows 1–10 there are unchanged) with the failure/rollback edges:

| # | From | To | Trigger event | Actor | Notes |
|---|---|---|---|---|---|
| 11 | `Executing` | `Blocked` | `DeveloperNeedsClarification` | Developer agent | See §3 — reuses `Blocked` rather than introducing an execution-specific blocked state. |
| 12 | `Publishing` | `AwaitingPublish` | `DeployFailed` | Deploy agent | See §5 — bounces back to the human gate rather than auto-retrying. |
| 13 | `Executing` | `Executing` (retry, no state change) | activity retry per Temporal policy | Temporal (transient failure) | Not a domain-visible transition — see §4. |
| 14 | `Review` | `Todo` | `ReviewRequestedChanges` | User | Founder-requested, added live: a reviewer can send a task back for another Developer pass instead of only approving. See §3a. |

No other edges are legal. In particular, there is no `Done → *` or `Production → *` edge: once a task reaches `Done`, the only further automatic move is the confirmation into `Production` (row 10 in [[003-Domain]]); anything discovered wrong after that point is a new Task, not a reopening of this one.

## 3. Blocked: unifying planning-time and execution-time clarification

[[003-Domain]] §3 flagged an open question: what happens when the *Developer* agent (not the Planner) gets stuck mid-execution? Two options were considered:

- Introduce a distinct `ExecutionBlocked` state with its own re-entry edge back into `Executing`.
- Reuse `Blocked`, always re-entering through `Inbox` (row 3 in [[003-Domain]]) regardless of which agent raised the question.

**Decision: reuse `Blocked`, always re-entering through `Inbox`.** This keeps `Blocked`'s re-entry semantics uniform — one edge, one meaning ("a human answered outstanding questions, planning resumes") — at the cost of the Planner briefly re-running even for a question that only the Developer agent needed answered. This trades a small amount of redundant Planner work for a materially simpler state machine, consistent with the operational-simplicity priority behind [[ADR-0001]]'s trade-offs.

Practical consequence for the Developer agent: if a Task re-enters `Todo` after having previously reached `Executing`, its `Worktree` and branch are **not** recreated — the scheduler must detect an existing non-deleted `Worktree` row for that task and resume the Developer agent against it rather than starting fresh. This is a requirement to carry into [[007-ExecutionEngine]], not yet implemented anywhere.

## 3a. Review → Todo: a deliberately different re-entry than Blocked

Founder-requested, found necessary live: dogfooding Forge on itself, a task reached `Review` with nothing actually deployed to inspect (the project's `PublishRecipe` was empty), and the only available action was "approve blindly" — not acceptable once a human is actually meant to look at the result before signing off. Row 14 (§2) adds `Review → Todo` (`ReviewRequestedChanges`) as the reviewer's other option besides approving.

**This deliberately does *not* reuse the `Blocked`/`Inbox` re-entry pattern from §3.** Re-entering through `Inbox` would re-run the Planner — but the plan (`Task.Description`/`AcceptanceCriteria`) was never the problem; the *implementation* was. `TaskWorkflow.RunAsync` instead wraps only the `AwaitingPublish → Publishing → Deploy → Review` portion in its own loop: a rejection goes `Review → Todo → Executing` directly, re-running only the Developer agent, against the **same worktree** (same rule as §3's Developer-resume case) with one addition — the reviewer's comment is recorded as a `ReviewRequestedChanges` event and read by the next `DevelopAsync` invocation (`AgentActivities.GetLatestReviewFeedbackAsync`), so the agent knows what specifically to fix rather than re-guessing. If that rework pass itself needs a genuine clarifying question, *that* still goes through `Blocked`/`Inbox` per §3 — only the "reviewer said do this differently" path skips Planner.

A task can bounce between `Review` and this rework loop more than once; `GetLatestReviewFeedbackAsync` only surfaces the most recent comment to the Developer agent, not the full history of every round - simplest thing that works today, and every round is still a permanent `Event` row for a human reading the timeline even if the agent only sees the latest.

Not every failure is domain-visible. A transient failure inside an agent's activity (a tool call timing out, a rate limit, a dropped connection) is retried by Temporal's built-in activity retry policy ([[ADR-0001]]) without the Task ever leaving `Executing` — row 13 above exists only to make this explicit, not because it's a real domain transition.

A failure only becomes domain-visible (`DeveloperNeedsClarification`, `DeployFailed`) when the agent itself determines the failure is not transient — i.e. retrying won't help because the problem is a genuine ambiguity or a real deploy error, not a flaky dependency. Where that line sits (how many retries, what counts as "not transient") is an implementation detail for [[005-Agents]] and [[007-ExecutionEngine]], not a domain concern.

## 5. Rollback Semantics

**`Publishing → AwaitingPublish` on `DeployFailed`.** When the Deploy agent's publish steps (code, DB migrations, other local changes — [[000-Vision]] UC-11) fail partway, the task returns to `AwaitingPublish` rather than advancing or self-retrying. This puts a human back in the loop before anything is retried, which matters because a partially-applied deploy (e.g. a migration that ran but a restart that didn't) is exactly the kind of state that shouldn't be blindly retried by an agent.

**Open item, not resolved here:** whether the Deploy agent's steps must be individually idempotent/reversible (so a retry after `DeployFailed` is safe) is a requirement for [[015-Deployment]], not a workflow-layer decision. Until that's specified, treat every `DeployFailed` as requiring human inspection before the user re-triggers `Publish`.

## 6. Timeout and Escalation Rules

**Decision: `Blocked` does not auto-timeout in v1.** A task can sit in `Blocked` indefinitely until a human answers. This was an open question in [[000-Vision]] §12; the simplest correct choice for v1 is no forced timeout — auto-escalating or auto-closing a blocked task risks silently discarding a real question, which is worse than an indefinitely-waiting card on a board. Revisit this once there's evidence of tasks going stale in `Blocked` for long enough to be a real problem (e.g. a "stale for N days" dashboard warning, not an automatic state change).

No other state has a timeout at v1. `AwaitingPublish` and `Review` are human gates by design ([[000-Vision]] §5, "Human approval where it matters") and are expected to wait as long as the human takes.

## 7. Task ↔ SubTask Relationship

**Decision: SubTasks are a checklist, not a parallel state machine.** A `SubTask.done` flag (per [[003-Domain]] INV-4) is informational for the Developer agent and for the UI's progress display — it does **not** gate the `Executing → AwaitingPublish` transition. The Developer agent is trusted to judge when the parent Task's acceptance criteria are met; requiring every sub-task to be marked done first would be a second, redundant gate on top of the acceptance criteria that already exist on the Task itself.

This is a default-to-simplicity choice, not a strong architectural commitment — if in practice agents move tasks forward with sub-tasks silently incomplete, hard-gating on `SubTask.done` is the first thing to reconsider.

## 8. Summary of Decisions Made in This Document

For quick reference — these were open questions elsewhere, now resolved here:

1. Execution-time clarification reuses `Blocked` (§3), not a new state.
2. A resumed task reuses its existing `Worktree`, it doesn't recreate one (§3).
3. Transient activity failures never surface as a domain state change (§4).
4. Failed deploys return to `AwaitingPublish`, not to a retry loop (§5).
5. `Blocked` does not auto-timeout in v1 (§6).
6. Incomplete sub-tasks do not block publishing (§7).
