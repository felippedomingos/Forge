# 002 — Architecture

## Status

Draft — Phase 3 (Architecture)

## 1. Component Diagram

```
 ┌─────────────┐        REST + WebSocket        ┌──────────────────┐
 │  Frontend   │ ─────────────────────────────▶ │   Forge API       │
 │  (React)    │ ◀───────────────────────────── │  (.NET Minimal API)│
 └─────────────┘                                 └─────────┬─────────┘
                                                             │ starts / signals
                                                             ▼
                                                   ┌───────────────────┐
                                                   │  Temporal Server   │  (ADR-0001)
                                                   │  (workflow engine) │
                                                   └─────────┬─────────┘
                                                             │ schedules activities
                                                             ▼
                                                   ┌───────────────────┐
                                                   │  Worker Process    │  (007-ExecutionEngine)
                                                   │  hosts Temporal     │
                                                   │  activities: Planner,│
                                                   │  Prioritizer, Developer,│
                                                   │  Deploy, Git agents │
                                                   └─────────┬─────────┘
                                                             │ MCP tool calls
                                          ┌──────────────────┼──────────────────┐
                                          ▼                  ▼                  ▼
                                   ┌─────────────┐   ┌──────────────┐   ┌──────────────┐
                                   │ Git/GitHub   │   │  Filesystem/  │   │  Model Router │
                                   │ plugin (010) │   │  Terminal     │   │  (008) → Claude│
                                   └─────────────┘   └──────────────┘   └──────────────┘

 ┌────────────────────────────────────────────────────────────────────────────┐
 │  PostgreSQL — Forge domain data (003-Domain entities) + Temporal persistence │
 │  (single instance, two logical databases: `forge`, `temporal`)              │
 └────────────────────────────────────────────────────────────────────────────┘
```

The Forge API is a **thin control plane**: it serves the frontend, translates HTTP/WebSocket calls into Temporal workflow starts/signals, and projects Temporal + domain data into read models for the UI. It holds no task-lifecycle state itself — see §4.

## 2. Why Event-Driven, Not "Agent Polls the Board"

[[000-Vision]] §5 commits to event-driven orchestration: moving a card doesn't call an agent directly, it produces an event that the workflow engine reacts to. Concretely, this is what [[ADR-0001]] already buys by choosing Temporal: a card move becomes a Temporal **Signal** delivered to that Task's running workflow (for human-gated transitions) or a **domain event** that the API translates into a signal/start call. No component polls the database for "what changed" — this isn't a separate architectural layer on top of Temporal, it's what Temporal workflows/signals *are*. There is no additional message broker (Kafka/RabbitMQ) in the v1 architecture; introducing one before there's evidence Temporal's own event model doesn't scale to Forge's needs would be premature complexity.

## 3. Why Linux-Only

Already established as a principle in [[000-Vision]] §5 and made concrete by [[ADR-0004]] (dedicated infrastructure, currently substituted by the founder's local machine). Nothing in this document changes that — it's restated here only to note that no component in the diagram above (Temporal, Postgres, the Worker process, Docker) has a Windows-specific dependency, so the constraint costs nothing architecturally.

## 4. Deployment Topology

**MVP (current, per the [[ADR-0004]] amendment):** every component runs as Docker containers on a single Linux machine, all 6 defined in the root `docker-compose.yml` — Postgres, Temporal, Temporal UI, Forge API, Worker, and Frontend (see [[015-Deployment]] §1). Single-node, single-database-instance.

**Post-MVP (v3 per [[016-Roadmap]]):** multiple Worker processes across multiple hosts, coordinated entirely through Temporal's task queues — a Worker is stateless with respect to *which* tasks it picks up, so horizontal scaling is adding more Worker processes pointed at the same Temporal server, not a rearchitecture.

**Real dedicated server:** once provisioned per [[ADR-0004]], the same container topology moves there unchanged — the local machine and the eventual dedicated server run the identical `docker compose` shape, differing only in network exposure (dedicated server may still be VPN-only, per the founder's Actiz infrastructure conventions, but that's a deployment concern, not an architecture one).

## 5. Failure Domains and Retry Boundaries

| Component fails | Consequence | Recovery |
|---|---|---|
| Worker process crashes mid-activity | The in-flight agent activity is lost | Temporal retries the activity per its configured policy — the workflow itself (and all state prior to the crashed activity) is untouched, since workflow state lives in Temporal/Postgres, not in the Worker process ([[ADR-0001]]). |
| Forge API crashes/restarts | No new HTTP requests served; frontend shows stale data until it reconnects | No task progress is lost — the API is stateless with respect to task lifecycle (§1); workflows keep advancing in Temporal independent of whether the API process is up. This is a deliberate consequence of the "thin control plane" decision, not an accident. |
| Temporal server down | No new workflow starts, no signals delivered, no activities scheduled — the whole system is paused | Full outage until Temporal is back; this is the single most critical component in the topology. No mitigation beyond "keep it running and monitored" is planned for v1 — a multi-node Temporal cluster is a post-MVP concern (see [[016-Roadmap]]). |
| Postgres down | Same as above — both Forge domain data and Temporal persistence live there | Same as above. Splitting Temporal's persistence onto a separate Postgres instance from Forge's domain data is an option to revisit if this single-point-of-failure becomes a real operational problem, not before. |
| A project's Worktree/branch gets corrupted (e.g. manual interference, disk issue) | That one Task's Developer/Deploy agent run fails repeatedly | No automatic recovery at v1 — treated as an operational incident requiring manual worktree cleanup, not a case the system self-heals. Flagged as a real gap, not silently assumed away. |

## 6. Cross-Cutting Concerns

- **Observability**: every agent invocation is a `Run` row ([[003-Domain]]) plus a Temporal activity execution with its own history entry — the live trace in [[000-Vision]] UC-9 is a projection of these two sources, not a separately-maintained log.
- **Cost tracking**: `Run.prompt_tokens` / `Run.completion_tokens` / `Run.cost_estimate`, rolled up per Task and per Project. Model cost metadata comes from the `Model` entity ([[003-Domain]]), populated per [[ADR-0003]] (Claude only at v1).
- **Audit trail**: the `Event` table plus Temporal's own workflow history together satisfy [[000-Vision]] §5's "complete audit trail" principle — this was the direct payoff of [[ADR-0001]]'s consequences.
