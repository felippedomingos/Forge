# 014 — Security

## Status

Draft — Phase 3 (Architecture)

## 1. Current State (Honest Baseline)

Nothing described below as "planned" exists yet beyond what's needed for local development. Specifically, as of this writing:

- **No AuthN/AuthZ**: [[012-API]] §4 already flags this — every endpoint is open, callable by anyone who can reach `http://localhost:5080`. Acceptable only because the API is bound to a single local machine ([[ADR-0004]] amendment) with no network exposure.
- **CORS** is configured permissively for local dev (`http://localhost:5173` only, but `AllowAnyHeader`/`AllowAnyMethod`) — fine for one trusted origin during development, not a policy that should survive to a real deployment unexamined.
- **Postgres credentials** (`forge` / `forge_local_dev`) are hardcoded fallback defaults in both `Forge.Api` and `Forge.Workflows` (`PersistenceActivities`) — plausible for a local-only dev database, never acceptable once real credentials exist.
- **No secrets manager integration** of any kind — there's nothing to manage yet, since no plugin has real credentials (the seeded `github` plugin row has an empty `configuration` JSONB).

This section exists so the gap is visible and tracked, not discovered later by accident.

## 2. Secrets Storage and Injection (Planned)

Once plugins carry real credentials ([[010-Plugins]] — e.g. a GitHub App token, Azure DevOps PAT), they must not live in the `plugins.configuration` JSONB column in plaintext. The concrete mechanism (environment injection at Worker startup vs. a dedicated secrets store like Vault) is not decided — Forge's bare-metal, single-machine deployment target ([[ADR-0004]]) makes a full secrets-manager dependency questionable at this scale; a simpler encrypted-at-rest column or an OS-level secrets file is more proportionate, but this is a genuine open decision, not resolved here.

## 3. Per-Project Credential Isolation

Each `Project` ([[003-Domain]]) has exactly one `git_provider_plugin_id`. Credentials for one project's Git provider must never be reachable by an agent activity running against a different project's task — today this isn't enforced anywhere in code (the activity stubs don't touch credentials at all yet), but it becomes a real requirement the moment [[005-Agents]]'s Developer/Git activities actually authenticate to GitHub.

## 4. Agent Permission Boundaries

[[009-MCP]] §4 already establishes the shape: tool access is fixed per agent role, not per task or user-configurable. The security-relevant restatement: a Planner activity should never be able to write to a Worktree, a Prioritizer should never get filesystem/terminal access at all, and a Deploy activity's terminal access should be scoped to publish-related commands, not an unrestricted shell. None of this is enforced in code yet since the activities don't call real tools — it's a requirement to carry into the real agent implementations, not a mechanism that exists today.

## 5. Blast Radius, Concretely

Worth stating plainly given [[000-Vision]]'s own framing (an autonomous agent with shell/Git access): if a Developer or Deploy activity is compromised or simply buggy, its blast radius today is bounded by [[007-ExecutionEngine]] §3's filesystem boundary (its own Worktree + the Worker's home directory) and by [[ADR-0004]]'s decision to keep Forge off the same network as Actiz's production infrastructure. Neither of those is a security control implemented in this codebase yet — they're operational/architectural boundaries, and this document should be revisited once real credentials and real tool execution exist, not treated as sufficient on its own.

## 6. Audit Trail

Already substantially covered by [[003-Domain]] §4's event catalog and [[002-Architecture]] §6 — every state transition is an `Event` row plus a Temporal workflow history entry. What's missing from a security-audit perspective specifically: no record yet of *who* configured a plugin's credentials, when, or from where. That's a gap to close once §1's "no AuthN" gap is closed — attribution is meaningless without knowing who's authenticated.

## 7. Open Questions

- Secrets storage mechanism (§2) — genuinely undecided.
- Whether AuthN should be added before or after any real deployment beyond the founder's own local machine — no other user exists yet to require it, so there's no pressure to decide this now, but it must happen before [[ADR-0004]]'s real dedicated server is reachable by anyone else.
