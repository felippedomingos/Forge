# ADR-0006: JWT Authentication, Admin-Created Accounts

## Status

Accepted

## Context

[[014-Security]] and every prior doc touching AuthN (`012-API` §4, `016-Roadmap`) deferred it entirely — the API has been fully open, acceptable only while Forge ran on one founder's own machine with no one else able to reach it. The founder confirmed real multi-user need ("precisamos de ter usuarios sim"), making this the point to actually build it rather than defer again.

Two decisions needed before writing code:

1. **Session mechanism.** A server-side session (cookie, stateful) is simpler to run since Forge has no separate token-issuing infrastructure, but the founder explicitly chose a stateless JWT instead when asked — the frontend holds the token (`localStorage`) and sends `Authorization: Bearer <token>` on every request, no server-side session store to manage.
2. **Account creation.** Self-service signup vs. admin-created accounts only. The founder chose admin-created — no public registration endpoint; a bootstrap mechanism creates the very first admin account (chicken-and-egg: nothing exists yet to authenticate the first `POST /users` call), then that admin creates everyone else.

## Decision

- **JWT, no refresh-token rotation at v1.** `POST /auth/login` (email + password, `BCrypt` verify) issues a single JWT (24h expiration, `FORGE_JWT_SECRET` env var for the signing key, same env-var-with-local-dev-fallback pattern as every other secret in this codebase) carrying `sub` (user id), `email`, and `role` claims. A user re-logs in after 24h; there is no silent renewal. This is a deliberate simplification for a small, non-public-facing team — full refresh-token rotation is real work with no concrete need for it yet, and adding it speculatively would be exactly the premature complexity [[000-Vision]]'s engineering priorities warn against. Revisit if session length in practice becomes a real friction point.
- **`Project.AllowAgentBypassPermissions`-style minimal authorization**: `Role == "Admin"` (a plain string check, not a claims/policy framework) gates `POST /users`; every other authenticated user can do everything else Forge's API already exposes. No per-endpoint permission matrix — there's no product requirement yet for finer-grained roles, and [[000-Vision]] §6's personas (Product Owner, Tech Lead, Operator, Contributor) don't map to differentiated *API* permissions today, only to differentiated *usage patterns* the UI doesn't yet distinguish either.
- **Bootstrap**: `POST /auth/bootstrap` is the one unauthenticated write endpoint, and only functions once — it 403s the instant `Users` has any row at all. Running it once (creating the founder's own Admin account) permanently closes that door; every subsequent account goes through `POST /users` as an authenticated Admin action.
- **Global default-deny**: `AddAuthorization` sets a fallback policy requiring an authenticated user for anything not explicitly marked `.AllowAnonymous()` — only `/auth/login` and `/auth/bootstrap` carry that attribute. This means every existing endpoint (`/projects`, `/tasks`, `/plugins`, `/git/branches`, ...) requires a valid JWT the moment this ships, not just newly-written ones - a global posture change, not an opt-in one.
- **WebSocket auth via query string**: `/ws/tasks/{id}` can't receive a custom `Authorization` header from the browser's native `WebSocket` API, so its JWT arrives as `?access_token=<jwt>` instead, extracted in `JwtBearerOptions.Events.OnMessageReceived` - the standard documented pattern for this exact limitation (shared with SignalR).

## Consequences

- Every frontend request now needs the token attached and a real "not authenticated" UX path (login screen, 401 handling) - not just a backend change. `frontend/src/lib/api.ts`'s `request()` helper is the one place this is threaded through.
- No password reset flow exists yet - an Admin would need to directly update a `PasswordHash` (e.g. via a future admin action, or a database update) if a user forgets their password. Not built because nothing has asked for it yet; a reasonable v2 addition once there's more than one or two real accounts.
- The 24h fixed expiration with no refresh means every user re-enters credentials daily. Fine for how few people use this today; worth revisiting the moment that's a real annoyance rather than a hypothetical one.
- `FORGE_JWT_SECRET` defaults to a hardcoded dev value when unset, exactly like `FORGE_CONNECTION_STRING` already does - acceptable for local-machine development ([[ADR-0004]]'s current substitution), but **must** be set to a real secret before this ever runs anywhere reachable by someone who shouldn't be able to forge tokens.
