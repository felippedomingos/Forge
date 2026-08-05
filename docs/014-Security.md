# 014 — Security

## Status

Draft — Phase 3 (Architecture)

## 1. Current State (Honest Baseline)

- **AuthN: implemented** ([[adr/ADR-0006]]) — JWT bearer tokens, `POST /auth/login`, admin-created accounts only (`POST /users`, gated on `Role == "Admin"`), a self-disabling `POST /auth/bootstrap` for the very first account. Every endpoint requires a valid token by default (`AddAuthorization`'s fallback policy) except the two auth endpoints themselves. This closed the single largest gap this document used to flag - not urgent-turned-mandatory the moment the founder confirmed real multi-user need.
- **AuthZ remains coarse**: one boolean distinction (`Role == "Admin"` gates `POST /users`; everything else any authenticated user can do). No per-project or per-action permission matrix - [[000-Vision]] §6's personas don't map to differentiated API permissions yet, only differentiated usage patterns the UI doesn't distinguish either. Revisit if that's ever a real complaint, not before.
- **`FORGE_JWT_SECRET` defaults to a hardcoded dev value when unset** — same pattern as `FORGE_CONNECTION_STRING` throughout this codebase, and the same caveat: **must** be set to a real secret before this runs anywhere reachable by someone who shouldn't be able to forge tokens.
- **CORS** is configured permissively for local dev (`http://localhost:5173` only, but `AllowAnyHeader`/`AllowAnyMethod`) — fine for one trusted origin during development, not a policy that should survive to a real deployment unexamined.
- **Postgres credentials** (`forge` / `forge_local_dev`) are hardcoded fallback defaults in both `Forge.Api` and `Forge.Workflows` (`PersistenceActivities`) — plausible for a local-only dev database, never acceptable once real credentials exist.
- **No secrets manager integration** of any kind — there's nothing to manage yet, since no plugin has real credentials (the seeded `github`/`azure-devops` plugin rows have empty `configuration` JSONB; the `az`/`gh` CLIs rely on this machine's own already-authenticated login, not a credential Forge itself stores).
- **Agent execution trust is now a real, per-project gate**, not just a documented intention: `Project.AllowAgentBypassPermissions` ([[009-MCP]] §4, [[005-Agents]] §4/§5) — false by default, must be explicitly enabled before the Developer/Deploy agents can edit files or run shell commands for a project's tasks at all.

This section exists so the gap is visible and tracked, not discovered later by accident.

## 2. Secrets Storage and Injection (Planned)

Once plugins carry real credentials ([[010-Plugins]] — e.g. a GitHub App token, Azure DevOps PAT), they must not live in the `plugins.configuration` JSONB column in plaintext. The concrete mechanism (environment injection at Worker startup vs. a dedicated secrets store like Vault) is not decided — Forge's bare-metal, single-machine deployment target ([[ADR-0004]]) makes a full secrets-manager dependency questionable at this scale; a simpler encrypted-at-rest column or an OS-level secrets file is more proportionate, but this is a genuine open decision, not resolved here.

## 3. Per-Project Credential Isolation

Each `Project` ([[003-Domain]]) has exactly one `git_provider_plugin_id`. Credentials for one project's Git provider must never be reachable by an agent activity running against a different project's task — today this isn't enforced anywhere in code (the activity stubs don't touch credentials at all yet), but it becomes a real requirement the moment [[005-Agents]]'s Developer/Git activities actually authenticate to GitHub.

## 4. Agent Permission Boundaries

**Implemented, in a simplified form.** [[009-MCP]] §4 originally proposed per-role MCP tool scoping (Planner never writes, Prioritizer never touches the filesystem, Deploy's shell access restricted to publish commands); what actually shipped is a coarser per-project trust flag instead (`Project.AllowAgentBypassPermissions` - see [[009-MCP]] §4's own note on this) - the founder's explicit simplification once faced with the real trade-off. Planner still never gets write access by construction (only `WebFetch`, narrowly, [[005-Agents]] §2); Developer and Deploy both refuse outright for any project not marked trusted, rather than running with a reduced/scoped tool set. This is a real, working boundary, just a coarser one than originally envisioned - fine as long as "trusted" or "not trusted" is the only distinction that matters, which is true today.

## 5. Blast Radius, Concretely

Worth stating plainly given [[000-Vision]]'s own framing (an autonomous agent with shell/Git access): if a Developer or Deploy activity is compromised or simply buggy, its blast radius today is bounded by [[007-ExecutionEngine]] §3's filesystem boundary (its own Worktree + the Worker's home directory) and by [[ADR-0004]]'s decision to keep Forge off the same network as Actiz's production infrastructure. Neither of those is a security control implemented in this codebase yet — they're operational/architectural boundaries, and this document should be revisited once real credentials and real tool execution exist, not treated as sufficient on its own.

## 6. Audit Trail

Already substantially covered by [[003-Domain]] §4's event catalog and [[002-Architecture]] §6 — every state transition is an `Event` row plus a Temporal workflow history entry. What's missing from a security-audit perspective specifically: `Event.Actor` records `"user:<id>"` / `"agent:<role>"` per [[003-Domain]] §4's schema, but nothing yet resolves a JWT's `sub` claim into that `user:<id>` at the point a human action (task creation, a move, an answer) is recorded - so today's events don't yet distinguish *which* authenticated user did something, only that some agent role did. Worth closing now that AuthN actually exists to attribute to.

## 7. Open Questions

- Secrets storage mechanism (§2) — genuinely undecided.
- Threading the authenticated user's identity into `Event.Actor` (§6) — AuthN exists now, this is no longer blocked on it, just not done yet.
- Password reset flow — doesn't exist. An Admin would need to directly update a `PasswordHash` today. Reasonable v2 addition once there's more than a couple of real accounts.
- 24h fixed JWT expiration, no refresh-token rotation ([[adr/ADR-0006]]) — a deliberate v1 simplification for a small team, revisit if daily re-login becomes a real annoyance rather than a hypothetical one.
