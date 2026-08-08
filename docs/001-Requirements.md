# 001 — Requirements

## Status

Draft — Phase 1 (Product), written after [[003-Domain]]/[[004-Workflow]]/architecture docs existed, so requirements below are checked against what's actually buildable rather than aspirational.

**Re-audited 2026-08-08** (founder asked "o que faltaria para finalizar o projeto Forge" - this doc hadn't been touched since the original stub phase and no longer reflected reality). Every FR below the "not implemented — stub" wording predates the Planner/Developer/Deploy/Git agents becoming real ([[016-Roadmap]] tracks that history day by day); this pass corrects each status against what's actually running today. The one requirement still genuinely open, unchanged since it was first decided: NFR-1's per-worker and global concurrency tiers.

## Purpose

Formal functional and non-functional requirements derived from [[000-Vision]], as testable SHALL/SHOULD statements, organized by lifecycle stage.

## 1. Functional Requirements — Task Management

- **FR-1.1**: A Task SHALL belong to exactly one Project. *(implemented — [[003-Domain]] INV-1)*
- **FR-1.2**: The system SHALL support creating a Task with only a `title`. *(implemented — `POST /tasks`, [[012-API]])*
- **FR-1.3**: The board SHALL support viewing Tasks across all Projects, or filtered to one. *(implemented — `GET /tasks?projectId=`)*
- **FR-1.4**: A Task's state SHALL only change via a transition explicitly defined in [[003-Domain]] §3 / [[004-Workflow]] §2. *(implemented as a structural property of `TaskWorkflow` — illegal transitions are unrepresentable, not merely rejected)*

## 2. Functional Requirements — Planner Agent

- **FR-2.1**: On entering `Inbox`, the Planner SHALL attempt to produce a description and acceptance criteria from the title and available project context. *(implemented — real `ClaudeCliProvider` call against the project's actual checkout, [[005-Agents]] §2)*
- **FR-2.2**: If the Planner cannot resolve the task, it SHALL transition the task to `Blocked` with its open questions recorded, never guess. *(implemented — exercised live hundreds of times across real projects, e.g. the 23 MKT tasks currently sitting `Blocked` on genuine questions as of this writing)*
- **FR-2.3**: Once a human answers a `Blocked` task's questions, the task SHALL return to `Inbox` and re-run planning. *(implemented — `POST /tasks/{id}/answers`, `AnswerQuestionsAsync` signal)*

## 3. Functional Requirements — Prioritizer Agent

- **FR-3.1**: Prioritization SHALL be scoped per-project, not globally across a user's projects. *(implemented — `BacklogSchedulerWorkflow` runs one instance per project, [[005-Agents]] §3)*
- **FR-3.2**: A Prioritizer failure SHALL NOT introduce a new domain-visible state — an unprioritized task simply stays in `Backlog`. *(implemented; also reads project memory before ranking, [[005-Agents]] §7)*

## 4. Functional Requirements — Developer Agent

- **FR-4.1**: On promotion to `Todo`, the Developer SHALL sync the project's root branch and create (or reuse, if resuming) a Worktree before executing. *(implemented — a git fetch/worktree-add failure routes to `Blocked` with the real git error rather than crashing, found live 2026-08-07/[[005-Agents]] §4)*
- **FR-4.2**: Execution progress SHALL be observable live, not only after completion. *(implemented — Postgres `LISTEN`/`NOTIFY` WebSocket, sub-second delivery validated live, [[007-ExecutionEngine]] §4)*
- **FR-4.3**: If the Developer needs clarification mid-execution, the task SHALL transition to `Blocked` via the same mechanism as planning-time blocks. *(implemented, exercised live continuously)*

## 5. Functional Requirements — Publish Gate and Deploy Agent

- **FR-5.1**: No deploy SHALL occur without an explicit human action moving the task from `AwaitingPublish` to `Publishing`. *(implemented — `RequestPublishAsync` signal, guarded)*
- **FR-5.2**: A failed deploy SHALL return the task to `AwaitingPublish` without automatic retry beyond the activity-level retry policy. *(implemented — [[004-Workflow]] §5)*

## 6. Functional Requirements — Review Gate and Git Agent

- **FR-6.1**: No task SHALL reach `Done` without an explicit human review approval. *(implemented — `ApproveReviewAsync` signal, guarded)*
- **FR-6.2**: On reaching `Done`, the Git agent SHALL push the task's branch, open a PR, and delete the Worktree. *(implemented — real `gh`/`az repos` PR creation; also detects when a branch is already integrated and skips a doomed PR attempt instead of leaving the task stuck, found live 2026-08-07/[[015-Deployment]] §3b)*

## 7. Functional Requirements — Production Confirmation

- **FR-7.1**: A task SHALL only reach `Production` on an explicit external confirmation, never inferred by Forge itself. *(implemented — `TaskWorkflow` polls the task's own PR merge status every 60s as the real confirmation signal, `ConfirmProductionAsync` remains as the manual escape hatch, [[015-Deployment]] §4; still no webhook-based CI/CD push, deliberately - polling was the simpler correct fit for a bare-metal deployment with no public endpoint)*

## 8. Non-Functional Requirements

- **NFR-1 (Concurrency)**: The system SHALL enforce configurable concurrency limits at the per-project, per-worker, and global tiers. *(partially implemented — per-project is real (`Project.MaxConcurrentExecuting`, and a genuine race in its enforcement was found and fixed live 2026-08-08, [[006-Scheduler]] §2); per-worker (Temporal's `maxConcurrentActivityExecutionSize`) and global (a cross-project LLM-call ceiling) are still exactly what they were at the original writing - decided, never configured. The only thing standing in for a global ceiling today is per-account usage tracking ([[adr/ADR-0005]]), which is visibility, not an enforced limit.)*
- **NFR-2 (Cost)**: Every agent invocation SHALL record token usage and cost against its `Run` row. *(implemented — populated by every real `ClaudeCliProvider` call; `GET /cost` and per-user Claude-account usage windows both read from it, [[011-Database]], [[adr/ADR-0005]])*
- **NFR-3 (Security)**: See [[014-Security]] in full — **AuthN is now met** (JWT bearer, [[adr/ADR-0006]], validated live end-to-end). AuthZ remains a single coarse Admin/non-Admin check, no per-project permissions - acceptable for the founder's own single-operator use, would need real design work before a second organization ever uses this. Secrets (`Project.GitCredential`, `ClaudeAccount.Token`) are plaintext in Postgres, a deliberate scope decision matching this posture, not an oversight.
- **NFR-4 (Auditability)**: Every state transition SHALL be reconstructable from the event/workflow history alone. *(implemented — `events` table + Temporal workflow history, [[011-Database]] §3)*

## 9. Traceability Matrix

| Requirement | Architecture / Domain | ADR | Implementation status |
|---|---|---|---|
| FR-1.1 – FR-1.4 | [[003-Domain]], [[012-API]] | — | Implemented |
| FR-2.1 – FR-2.3 | [[005-Agents]] §2, [[004-Workflow]] §3 | [[ADR-0003]] | Implemented |
| FR-3.1 – FR-3.2 | [[005-Agents]] §3, [[006-Scheduler]] | — | Implemented |
| FR-4.1 – FR-4.3 | [[005-Agents]] §4, [[007-ExecutionEngine]] | [[ADR-0002]] | Implemented |
| FR-5.1 – FR-5.2 | [[004-Workflow]] §5, [[015-Deployment]] | [[ADR-0001]] | Implemented |
| FR-6.1 – FR-6.2 | [[005-Agents]] §6, [[010-Plugins]] | [[ADR-0002]] | Implemented |
| FR-7.1 | [[003-Domain]] row 10, [[015-Deployment]] §4 | — | Implemented |
| NFR-1 | [[006-Scheduler]] §2 | [[ADR-0001]] | Per-project enforced; per-worker/global still not implemented |
| NFR-2 | [[011-Database]], [[008-ModelRouter]] | [[ADR-0003]] | Implemented |
| NFR-3 | [[014-Security]] | [[adr/ADR-0006]] | AuthN implemented; AuthZ coarse; secrets plaintext (accepted) |
| NFR-4 | [[011-Database]] §3 | [[ADR-0001]] | Implemented |
