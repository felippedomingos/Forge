# 005 — Agents

## Status

Not started — Phase 2/3

## Purpose

Specification for each agent role: inputs, outputs, tools available, and the exact boundary of its responsibility (an agent owns exactly one lifecycle stage).

## Planned Outline

- Planner agent (investigation sources: local repo, docs, web, Azure DevOps, az cli; question/blocked protocol)
- Prioritizer agent (ordering signals, cross-project prioritization)
- Developer agent (sync, worktree, branch, implementation loop, test/build, live trace)
- Deploy agent (publish gate execution: code, DB migrations, other local changes)
- Git agent (commit, push, PR, worktree cleanup)
- Per-project, per-agent memory (what each agent learns and retains, e.g. "this project uses XAF")
- Agent-to-tool contract (how an agent calls MCP tools; error/timeout handling)
