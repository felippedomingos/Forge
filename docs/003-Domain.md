# 003 — Domain Model

## Status

Not started — Phase 2 (Domain)

## Purpose

The authoritative domain model: entities, aggregates, states, events and invariants. This is the source of truth for terms used loosely in [[000-Vision]].

## Planned Outline

- Entities: Project, Task, SubTask, AcceptanceCriterion, Worker, Worktree, Run, Event, Plugin, Model, User
- Aggregates and invariants (e.g. a Task always belongs to exactly one Project)
- Task state machine (formal definition, valid transitions, guards)
- Event catalog (TaskCreated, TaskMoved, AgentStarted, AgentCompleted, PublishRequested, ...)
- Domain services vs. agent responsibilities
