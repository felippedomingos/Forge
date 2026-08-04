# ADR-0001: Temporal as the Workflow/Orchestration Engine

## Status

Accepted

## Context

Forge needs to turn task state transitions into durable agent executions: a Developer agent run can take hours, must survive a worker crash and resume where it left off, and every step must be reconstructable from history (see [[000-Vision]] §5, "Complete audit trail").

Two options were evaluated:

- **Hangfire** — in-process, .NET-native, no extra infrastructure beyond the existing PostgreSQL instance. Simpler to operate on bare-metal, but retries/timers/long-running state are built by hand.
- **Temporal** — purpose-built durable execution engine. Native support for long-running workflows, automatic retries, timers and signals, and a full event history per workflow execution. Requires standing up and operating a Temporal server as a new piece of infrastructure.

The lower-friction path for an MVP would have been Hangfire. The founder explicitly chose Temporal, accepting the added operational cost of running and monitoring a Temporal cluster on bare-metal Linux from day one, in exchange for correctness guarantees on long-running, resumable agent workflows and human-gated steps (Publish, Review).

## Decision

Temporal is the workflow engine for Forge, starting at MVP.

- Each Task's lifecycle ([[004-Workflow]]) is modeled as a Temporal Workflow.
- Each Agent invocation ([[005-Agents]]) runs as a Temporal Activity, with retry/backoff configured per activity type.
- Human-gated transitions (`Awaiting Publish → Publish`, `Review → Done`) are modeled as Temporal Signals awaited by the workflow.
- `Blocked` timeouts ([[004-Workflow]]) are modeled as Temporal Timers.

## Consequences

- Long-running Developer agent executions survive worker/process crashes and resume from the last completed activity, without custom checkpointing code.
- Workflow event history satisfies most of the audit trail requirement from [[000-Vision]] for free — no separate event-sourcing table needed purely for task history (though [[011-Database]] may still project it into a queryable table).
- Timers and signals map directly onto the `Blocked` and human-gate semantics in [[004-Workflow]], instead of being hand-rolled.
- Requires a running Temporal server (self-hosted or Temporal Cloud) on the dedicated infrastructure defined in [[ADR-0004]] before any workflow can execute — this is a hard prerequisite for the first end-to-end test.
- Adds a new operational component (Temporal server, its own datastore, its own monitoring) that the team must run and understand on Linux, beyond what exists for Actiz today.
- Backend implementation uses the Temporal .NET SDK (`Temporalio`) rather than plain background jobs.
