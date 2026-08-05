# ADR-0005: Claude Code CLI (Interactive Auth) as the Agent Invocation Mechanism

## Status

Accepted

## Context

[[ADR-0003]] decided Forge's Model Router would ship with exactly one provider at v1 (Claude), but left open *how* that provider is actually invoked — the natural default assumption was a raw Anthropic Console API integration (an API key, direct SDK calls), which requires provisioning a separate API credential for Forge's own backend before any agent activity could run for real.

The founder pointed out an alternative: invoke the **Claude Code CLI itself** as a subprocess (`claude -p "<prompt>" --output-format ...`), authenticated via the interactive Claude.ai subscription login already present on the development machine (confirmed working — this exact mechanism already powers the local cron backstop in `scripts/cron-continue.sh`). This removes the separate-API-credential prerequisite entirely for single-account use, and it's arguably a more faithful reading of the founder's original scope: Forge was described from the start as orchestrating *Claude Code*, not a bespoke API integration built from scratch.

A related idea was raised: running multiple Claude accounts with automatic failover when one hits a usage limit. Investigated and confirmed technically feasible — Claude Code CLI respects a `CLAUDE_CONFIG_DIR` environment variable that isolates credentials/config per directory, independent of `$HOME` (verified: pointing `HOME` at an empty scratch directory produces a clean "not logged in" state, and the CLI binary itself references `CLAUDE_CONFIG_DIR` for exactly this kind of isolation). This means N accounts can coexist on one machine as N config directories, each logged in once.

## Decision

- Forge's agent activities (Planner, Developer, and eventually Deploy/Git where relevant) invoke the **Claude Code CLI as a subprocess** (`claude -p`, non-interactive/print mode), not the raw Anthropic API/SDK directly.
- **v1 (now): single account.** The CLI uses whatever account is already logged in via the default `CLAUDE_CONFIG_DIR` (i.e., no override) on the machine running the Worker process.
- **Multi-account fallback is designed for, not built yet.** The Model Router's `Provider` abstraction ([[008-ModelRouter]]) gets a `ClaudeCliProvider` implementation parameterized by a `CLAUDE_CONFIG_DIR` path; a future `ClaudeCliProviderPool` (or equivalent) would hold N configured profiles and rotate to the next on a detected quota/rate-limit failure from the CLI's exit code/stderr. **Not implemented until the founder has actually logged in N accounts** — each additional account requires an interactive OAuth device/browser flow that only a human can complete; this cannot be scripted end-to-end by an agent.

## Consequences

- Removes the "Forge needs its own Anthropic API credential" blocker that had paused this build — the Planner/Developer agents can be implemented against a real LLM immediately, using the existing machine's login.
- Ties Forge's agent execution to whatever machine holds that CLI login and its usage limits/rate limits (a Claude.ai subscription's usage caps, not a Console pay-as-you-go pool) — acceptable for the founder's own single-machine development use per [[ADR-0004]], but this is a real constraint to revisit before any multi-user or higher-throughput scenario.
- Multi-account rotation, when built, spreads load across multiple *personal* subscription logins rather than a single scalable API billing account — this is a reasonable fit for a solo founder's own tool, but is a meaningfully different operational model from a typical SaaS backend's LLM billing, and is worth the founder independently confirming stays within Anthropic's usage policies for his account(s) before scaling past one.
- The `ClaudeCliProvider` becomes a subprocess-management concern (spawning `claude -p`, parsing its output, handling timeouts/kills) inside an Activity, not a simple HTTP client call — slightly more moving parts than a pure API integration, in exchange for zero additional credential provisioning.
- API-key-based providers (Console, or other vendors) remain a valid future `Provider` implementation for anyone who *does* want that model — this ADR doesn't foreclose it, it just isn't v1's path.
