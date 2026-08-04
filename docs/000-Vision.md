# 000 — Vision

## Status

Draft — Phase 1 (Product)

## 1. Problem Statement

Modern software delivery sits on two disconnected layers:

- **Project management tools** (Jira, Linear, Azure DevOps Boards) are excellent at representing *state* — what needs to happen, in what order, and who is accountable — but they know nothing about the code itself.
- **AI coding tools** (Claude Code, Cursor, Codex, Cline) are excellent at *acting* on a codebase — reading it, changing it, testing it — but they have no concept of a backlog, priority, or lifecycle. Every session starts cold, scoped to whatever the human typed.

Between these two layers sits a large amount of manual translation: a human reads a card, opens a terminal, gives an agent context, watches it work, decides when it's done, moves the card, opens a PR, merges it, and updates the board again. That translation work is repetitive, error-prone, and does not scale past a handful of tasks or repositories.

Forge exists to remove that translation layer.

## 2. Vision Statement

> Forge is an AI-native software factory that turns a task board into an autonomous execution pipeline — where creating a task with nothing but a title is enough to trigger investigation, planning, implementation, review and deployment, with humans approving only the decisions that matter.

The unit of work in Forge is not a ticket to be read by a human and acted on later — it is a task that *drives* a pipeline of specialized agents, each responsible for one stage of the software lifecycle, coordinated by an event-driven workflow engine rather than by a human moving cards by hand.

## 3. Why Now

Three things changed recently that make this feasible:

1. **Coding agents crossed a capability threshold.** Tools like Claude Code can read an entire repository, understand architecture, run tests, use a terminal, call external APIs and iterate — for hours — without step-by-step hand-holding.
2. **MCP (Model Context Protocol) standardized tool access.** An agent no longer needs bespoke integration code to talk to Git, Azure DevOps, a database or a browser — it can be handed a toolset.
3. **Git worktrees make parallel, isolated execution cheap.** Multiple agents can work on multiple tasks against the same repository concurrently without branch-switching collisions.

Forge is the orchestration layer that ties these three developments into a coherent product, instead of leaving every team to wire it together by hand per project.

## 4. Non-Goals

To keep scope honest, Forge explicitly does **not** try to:

- Replace human judgment on what to build — task creation, priority calls, publish/no-publish and review approval remain human decisions.
- Be a general-purpose project management tool (roadmaps, OKRs, timesheets). It focuses on the task → code → production lifecycle.
- Support closed, proprietary CI/CD platforms as a hard dependency — Forge assumes Git as the source of truth and treats CI/CD as a plugin.
- Run on Windows or macOS as a server. Forge's execution runtime is Linux-first (see [[002-Architecture]], ADR on OS choice).
- Lock into a single LLM vendor. Model choice is a routing decision (see [[008-ModelRouter]]), not an architectural one.

## 5. Core Principles

These principles constrain every architectural decision made later in [[002-Architecture]]:

- **AI-first, not AI-assisted.** Agents are first-class actors in the workflow, not autocomplete for a human typing in an IDE.
- **Event-driven, not polling.** State transitions emit events; agents react to events dispatched by a workflow engine. No agent watches the board (see ADR on event-driven orchestration).
- **Human approval where it matters.** Publishing to production and marking work reviewed are always human-gated. Investigation, planning and implementation are not.
- **Git-native.** Every unit of work maps to a branch, a worktree and eventually a PR. There is no shadow state outside Git for the code itself.
- **Linux-first.** The execution runtime (workers, worktrees, containers) targets Linux servers exclusively.
- **Multi-model.** Any agent role can be served by any capable LLM; the system routes by capability, cost and availability.
- **Plugin-based.** Git providers, issue trackers, cloud CLIs, databases and deployment targets are plugins, not hardcoded integrations.
- **Transparent execution.** Every action an agent takes (files read, commands run, tokens spent, reasoning steps) is visible in real time against the task, not hidden in a log file.
- **Complete audit trail.** Every state transition, every agent decision and every human approval is recorded as an event and never silently overwritten.

## 6. Personas

| Persona | Description | Primary need |
|---|---|---|
| **Product Owner / Founder** | Creates tasks with a short title, sets priority, approves publishing and reviews finished work. | Minimum friction to go from idea to shipped feature. |
| **Tech Lead** | Reviews agent-produced plans and code, resolves ambiguity the Planner agent flags, occasionally intervenes in execution. | Confidence that agents follow the project's real architecture and conventions. |
| **Operator / DevOps** | Cares about how publishing, migrations and rollbacks are executed. | Predictable, observable, reversible deployments. |
| **Contributor (future / OSS)** | Extends Forge itself — new plugins, new agent roles, new model providers. | A stable plugin SDK and clear extension points. |

## 7. Core Concepts

- **Project** — maps 1:1 to a Git repository. All tasks belong to exactly one project.
- **Task** — the unit of work. Represents an intended change to a project's source code (feature, fix, chore). Has a title, a state, an owning project, optional sub-tasks, acceptance criteria, and a full execution history.
- **State** — a task's position in the lifecycle (see [[004-Workflow]] for the full state machine): `Inbox → Backlog → Blocked → Todo → Executing → Awaiting Publish → Publishing → Review → Done → Production`.
- **Agent** — a specialized AI worker bound to exactly one stage of the lifecycle (Planner, Prioritizer, Developer, Deploy, Git). Agents do not span stages.
- **Event** — an immutable fact ("TaskMoved", "TaskCreated", "AgentCompleted") that drives the workflow engine. The board never calls an agent directly.
- **Worker** — an isolated execution environment (its own home directory, its own Git worktree, its own resource limits) where a Developer agent runs.
- **Worktree** — a `git worktree` checkout created per in-flight task so multiple tasks can be executed concurrently against the same repository without collisions.
- **Plugin** — an integration adapter (Git provider, issue tracker, cloud CLI, database, deployment target) implementing a stable Forge interface.
- **Model Router** — the component that decides which LLM serves a given agent invocation, based on capability required, cost ceiling and availability.

## 8. Use Cases

These map directly to the requirements the founder specified when proposing Forge, and will be expanded into formal functional requirements in [[001-Requirements]].

**UC-1 — Cross-project visibility.** As a Product Owner, I can view all tasks across all projects in one board, or filter to a single project, so I don't need to context-switch between repositories to know what's in flight.

**UC-2 — Task-to-code binding.** As a Tech Lead, every task I create is bound to exactly one project/repository, so execution always knows which codebase to operate on.

**UC-3 — Autonomous investigation from a title alone.** As a Product Owner, I can create a task with only a title. A Planner agent reads it, pulls current context from the project (source, docs, and — when needed — external sources like a website, Azure DevOps, or `az cli`), and produces a description and acceptance criteria without further input from me.

**UC-4 — Escalation on ambiguity.** As a Planner agent, when I cannot resolve what's being asked, I move the task to `Blocked` and record the specific questions I need answered, instead of guessing.

**UC-5 — Resume after clarification.** As a Product Owner, once I answer the Planner's questions, I move the task back to `Inbox` so planning resumes with that new information.

**UC-6 — Prioritization as its own stage.** As a Prioritizer agent, once a task is fully planned and sitting in `Backlog`, I order it against everything else in the backlog, independent of the planning step.

**UC-7 — Promotion to execution.** As a system, once a task is prioritized, a dedicated agent moves it to `Todo`, decoupling "ready to plan" from "ready to build."

**UC-8 — Isolated execution.** As a Developer agent, when a task enters `Todo`, I sync the project's root branch (`main`/`develop`/`dev`), create a worktree, cut a feature branch, and execute — without touching any other in-flight task's workspace.

**UC-9 — Live observability.** As a Tech Lead, I can open any executing task and see the agent's live console output, files changed, reasoning trace, tokens spent and cost, the same way I'd watch a remote pair-programming session.

**UC-10 — Human-gated publish.** As an Operator, once execution finishes, the task waits in `Awaiting Publish`. Nothing is deployed until I explicitly move it to `Publish`.

**UC-11 — Controlled release.** As a Deploy agent, when a task is moved to `Publish`, I apply code, database and any other required local changes, then move the task to `Review` for the user to validate.

**UC-12 — Closing the loop.** As a Product Owner, once I've verified the change in `Review`, I move the task to `Done`. This triggers a Git agent to commit, push, open a PR and delete the worktree.

**UC-13 — Production confirmation.** As a system, once the downstream CI/CD pipeline confirms the deployment succeeded, the task automatically moves to `Production`.

## 9. High-Level Flow

```
Inbox ──────► Planner Agent ─────┬──► Blocked (needs answers) ──► back to Inbox
                                  │
                                  └──► Backlog ──► Prioritizer Agent ──► Todo
                                                                          │
                                                                          ▼
                                                                  Developer Agent
                                                            (sync → worktree → branch → build)
                                                                          │
                                                                          ▼
                                                              Awaiting Publish (human gate)
                                                                          │
                                                                    user moves ──► Publish
                                                                          │
                                                                          ▼
                                                                   Deploy Agent
                                                                          │
                                                                          ▼
                                                                       Review (human gate)
                                                                          │
                                                                    user moves ──► Done
                                                                          │
                                                                          ▼
                                                                     Git Agent
                                                          (commit, push, PR, delete worktree)
                                                                          │
                                                                          ▼
                                                              Production (pipeline confirms)
```

Every arrow above is an **event**, not a direct function call — see [[004-Workflow]] and the ADR on event-driven orchestration for why the board never invokes an agent directly.

## 10. Success Criteria for v1.0

- A task created with only a title reaches `Backlog` with a usable description and acceptance criteria, without human intervention, for at least the common case (bug fix / small feature in a known project).
- A Tech Lead can watch any `Executing` task live and understand what the agent is doing without reading raw logs.
- No code reaches `Production` without an explicit human action at `Publish` and at `Done`.
- The same board shows tasks from at least two distinct projects/repositories with correct isolation between their worktrees and branches.
- Every state transition is reconstructable from the event log alone.

## 11. Glossary

See [[003-Domain]] for the authoritative domain model; terms here are the plain-language versions used in product conversations.

- **Board** — the Kanban view over tasks; one of several possible views over the same underlying task data (list, timeline, tree are others, planned post-v1).
- **Root branch** — the branch a project treats as its integration target (`main`, `develop`, or `dev`), synced before every new worktree is created.
- **Root cause vs. symptom (Planner)** — the Planner agent's job is to interpret a title against real project context, not to accept it literally.

## 12. Open Questions

Tracked here until resolved into an ADR or into [[001-Requirements]]:

- How many concurrent workers per project should v1 support by default?
- Does `Blocked` support partial answers (resume planning incrementally) or does it always require a full round-trip to `Inbox`?
- What happens to a task's worktree if a task is blocked for a long time — does it get cleaned up and recreated on resume?
