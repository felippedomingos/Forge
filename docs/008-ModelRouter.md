# 008 — Model Router

## Status

Draft — Phase 3 (Architecture)

## 1. Provider Interface

A single abstraction every agent activity ([[005-Agents]]) calls through, never a vendor SDK directly:

```
interface ModelProvider {
  string Name;                  // "anthropic-claude", later "openai-gpt", ...
  CapabilityTier Tier;          // matches the Model.capability_tier column (011-Database)
  decimal CostPerToken;
  Task<AgentResult> Invoke(AgentRequest request, CancellationToken ct);
}
```

`AgentRequest` carries the agent role, the prompt/context, and the MCP tool set that role is allowed to use ([[009-MCP]], [[005-Agents]] §8). `AgentResult` carries the output plus token usage, feeding the `Run` row directly ([[003-Domain]], [[007-ExecutionEngine]] §5).

Per [[ADR-0003]], exactly one implementation exists at v1: `ClaudeCliProvider`. Per [[ADR-0005]], it wraps the **Claude Code CLI as a subprocess** (`claude -p`, interactive-subscription auth) rather than a raw Anthropic API/SDK call — removing the separate API-credential prerequisite for single-account use. The interface is designed so a second implementation (another provider, or a multi-account `ClaudeCliProviderPool` per [[ADR-0005]]'s fallback design) is additive — never a change to the interface itself or to agent code.

## 2. Routing Inputs (Designed Now, Exercised Later)

With only one enabled provider, there's nothing to route *between* yet — but the router still takes the following inputs today, so the seam is real rather than aspirational:

- **Agent role** — some roles may warrant a different capability tier than others once more than one provider exists (e.g. Planner's investigative reasoning vs. Git agent's comparatively mechanical push/PR calls).
- **Cost ceiling** — a per-Project or per-Task budget cap, checked against `Model.cost_per_token` before invocation.
- **Provider availability / rate limits** — with one provider, this only matters as "is Claude currently reachable," not as a choice between providers.

None of this logic does anything interesting until a second `Model` row exists — that's expected, not a sign the abstraction is premature; it's the seam [[ADR-0003]] explicitly paid for up front.

## 3. Fallback and Failover

**Deferred past v1** per [[ADR-0003]]. Today, a Claude outage or rate-limit means the affected activity retries per [[006-Scheduler]] §3's policy against the same provider — there is no second provider to fail over to yet. When a second provider is added, this section gets a real failover policy (e.g. "on 3 consecutive provider errors, retry once against the next-highest-tier alternate provider before surfacing a domain-level failure"); until then, documenting a failover policy for a single-provider system would be fiction.

## 4. Per-Project / Per-Agent Overrides

`agent_memory` ([[011-Database]]) is the natural place for a project to pin a specific model preference per agent role once more than one provider exists (e.g. "use a cheaper tier for this project's Prioritizer, it's a low-stakes internal tool") — not implemented at v1 since there's only one provider to pin to, but the storage mechanism already exists without inventing a new table.

## 5. Cost Tracking Integration

The router is the single place `Run.cost_estimate` gets computed ([[003-Domain]], [[007-ExecutionEngine]] §5) — every `Invoke` call writes token usage and cost back to the `Run` row before returning, so cost data can never drift from what the router actually charged. [[012-API]]'s cost endpoints read from `Run`/`Model` directly; they don't recompute cost independently.
