# 001 — Requirements

## Status

Draft — Phase 1 (Product), written after [[003-Domain]]/[[004-Workflow]]/architecture docs existed, so requirements below are checked against what's actually buildable rather than aspirational.

## Purpose

Formal functional and non-functional requirements derived from [[000-Vision]], as testable SHALL/SHOULD statements, organized by lifecycle stage.

## 1. Functional Requirements — Task Management

- **FR-1.1**: A Task SHALL belong to exactly one Project. *(implemented — [[003-Domain]] INV-1)*
- **FR-1.2**: The system SHALL support creating a Task with only a `title`. *(implemented — `POST /tasks`, [[012-API]])*
- **FR-1.3**: The board SHALL support viewing Tasks across all Projects, or filtered to one. *(implemented — `GET /tasks?projectId=`)*
- **FR-1.4**: A Task's state SHALL only change via a transition explicitly defined in [[003-Domain]] §3 / [[004-Workflow]] §2. *(implemented as a structural property of `TaskWorkflow` — illegal transitions are unrepresentable, not merely rejected)*

## 2. Functional Requirements — Planner Agent

- **FR-2.1**: On entering `Inbox`, the Planner SHALL attempt to produce a description and acceptance criteria from the title and available project context. *(not implemented — stub always succeeds with placeholder text, [[005-Agents]] §2)*
- **FR-2.2**: If the Planner cannot resolve the task, it SHALL transition the task to `Blocked` with its open questions recorded, never guess. *(workflow-level mechanism implemented; the actual "can't resolve" judgment doesn't exist since the Planner isn't real yet)*
- **FR-2.3**: Once a human answers a `Blocked` task's questions, the task SHALL return to `Inbox` and re-run planning. *(implemented — `POST /tasks/{id}/answers`, `AnswerQuestionsAsync` signal)*

## 3. Functional Requirements — Prioritizer Agent

- **FR-3.1**: Prioritization SHALL be scoped per-project, not globally across a user's projects. *(decided — [[005-Agents]] §3; not implemented)*
- **FR-3.2**: A Prioritizer failure SHALL NOT introduce a new domain-visible state — an unprioritized task simply stays in `Backlog`. *(decided, not yet exercised since no real Prioritizer exists)*

## 4. Functional Requirements — Developer Agent

- **FR-4.1**: On promotion to `Todo`, the Developer SHALL sync the project's root branch and create (or reuse, if resuming) a Worktree before executing. *(not implemented — stub does no git operations yet)*
- **FR-4.2**: Execution progress SHALL be observable live, not only after completion. *(not implemented — [[007-ExecutionEngine]] §4's two-channel design exists on paper only)*
- **FR-4.3**: If the Developer needs clarification mid-execution, the task SHALL transition to `Blocked` via the same mechanism as planning-time blocks. *(implemented at the workflow level — `DeveloperNeedsClarification` path)*

## 5. Functional Requirements — Publish Gate and Deploy Agent

- **FR-5.1**: No deploy SHALL occur without an explicit human action moving the task from `AwaitingPublish` to `Publishing`. *(implemented — `RequestPublishAsync` signal, guarded)*
- **FR-5.2**: A failed deploy SHALL return the task to `AwaitingPublish` without automatic retry beyond the activity-level retry policy. *(implemented — [[004-Workflow]] §5)*

## 6. Functional Requirements — Review Gate and Git Agent

- **FR-6.1**: No task SHALL reach `Done` without an explicit human review approval. *(implemented — `ApproveReviewAsync` signal, guarded)*
- **FR-6.2**: On reaching `Done`, the Git agent SHALL push the task's branch, open a PR, and delete the Worktree. *(not implemented — `GitFinalizeAsync` is a no-op stub)*

## 7. Functional Requirements — Production Confirmation

- **FR-7.1**: A task SHALL only reach `Production` on an explicit external confirmation, never inferred by Forge itself. *(implemented at the workflow level — `ConfirmProductionAsync`; no real CI/CD integration sends it yet, [[015-Deployment]] §4)*

## 8. Non-Functional Requirements

- **NFR-1 (Concurrency)**: The system SHALL enforce configurable concurrency limits at the per-project, per-worker, and global tiers. *(decided — [[006-Scheduler]] §2; not enforced in code, since only one Worker exists and nothing yet contends)*
- **NFR-2 (Cost)**: Every agent invocation SHALL record token usage and cost against its `Run` row. *(schema exists — [[011-Database]]; nothing populates it yet since no real LLM call happens)*
- **NFR-3 (Security)**: See [[014-Security]] in full — as of this writing, NFR-3 is **not met**: no AuthN exists, and this is accepted only because Forge runs on a single local machine with no network exposure.
- **NFR-4 (Auditability)**: Every state transition SHALL be reconstructable from the event/workflow history alone. *(implemented — `events` table + Temporal workflow history, [[011-Database]] §3)*

## 9. Traceability Matrix

| Requirement | Architecture / Domain | ADR | Implementation status |
|---|---|---|---|
| FR-1.1 – FR-1.4 | [[003-Domain]], [[012-API]] | — | Implemented |
| FR-2.1 – FR-2.3 | [[005-Agents]] §2, [[004-Workflow]] §3 | [[ADR-0003]] | Workflow mechanism implemented; agent logic stubbed |
| FR-3.1 – FR-3.2 | [[005-Agents]] §3, [[006-Scheduler]] | — | Decided, not implemented |
| FR-4.1 – FR-4.3 | [[005-Agents]] §4, [[007-ExecutionEngine]] | [[ADR-0002]] | Workflow mechanism implemented; agent logic stubbed |
| FR-5.1 – FR-5.2 | [[004-Workflow]] §5, [[015-Deployment]] | [[ADR-0001]] | Workflow mechanism implemented; Deploy logic stubbed |
| FR-6.1 – FR-6.2 | [[005-Agents]] §6, [[010-Plugins]] | [[ADR-0002]] | Workflow mechanism implemented; Git logic stubbed |
| FR-7.1 | [[003-Domain]] row 10, [[015-Deployment]] §4 | — | Workflow mechanism implemented; no real integration |
| NFR-1 | [[006-Scheduler]] §2 | [[ADR-0001]] | Decided, not enforced |
| NFR-2 | [[011-Database]], [[008-ModelRouter]] | [[ADR-0003]] | Schema exists, unpopulated |
| NFR-3 | [[014-Security]] | — | Not met (accepted for local-only dev) |
| NFR-4 | [[011-Database]] §3 | [[ADR-0001]] | Implemented |
