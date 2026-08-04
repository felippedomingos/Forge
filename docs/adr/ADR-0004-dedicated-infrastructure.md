# ADR-0004: Dedicated Infrastructure for Forge, Isolated from Actiz Production

## Status

Accepted

## Context

Forge's Developer and Deploy agents will have shell, Git, and — eventually — database access to whatever project they operate on ([[000-Vision]] §7, "Worker"). Actiz's own infrastructure runs three isolated clusters (QA, PROD in São Paulo, PROD-LATIN in Colombia) serving real LIMS customers under ISO 17025 compliance constraints. Running an experimental, autonomous coding agent on the same network/VPN as those clusters risks it affecting systems that serve real customers, even unintentionally.

## Decision

Forge runs on new, dedicated Linux server(s), isolated from the Actiz QA/PROD/PROD-LATIN clusters — separate network segment/VPN (or none, if not required) and separate credentials, with no shared access path into Actiz production infrastructure.

## Consequences

- Zero blast radius between a Forge agent bug/runaway execution and any Actiz customer-facing environment.
- Forge's own infrastructure can be torn down and rebuilt freely during early development without any change-control process that would apply to Actiz production.
- This is an external prerequisite: provisioning the new server/VM is an action the founder must take outside of this session — it cannot be provisioned by an agent running inside this repository. Deployment-related work ([[015-Deployment]]) and any real end-to-end Deploy-agent test are blocked until this VM exists and its access (SSH, Temporal server per [[ADR-0001]], PostgreSQL) is available.
- Open question carried into [[015-Deployment]]: when Forge's own Developer agents work on Forge's own codebase (self-hosting), they will run on this same dedicated box. Whether that requires yet another layer of separation (agent-modifying-its-own-host) is not yet decided.

## Amendment (2026-08-04): local machine as interim substitute

Before provisioning the real dedicated server, the founder authorized using his local development machine as a stand-in, specifically to unblock architecture validation without waiting on infra procurement:

- All Actiz-related Docker Compose stacks running on the machine (`infra` — ActizOps local dev, `actiz-staging` — WordPress staging) were stopped (`docker compose stop`, not removed — fully reversible) to free ports and resources.
- Forge's local stack (`docker/local/docker-compose.yml`) now runs Postgres 16, Temporal (`temporalio/auto-setup`) and Temporal UI, all bound to `127.0.0.1` only. Validated working: Postgres healthy, Temporal serving (`cluster health` → `SERVING`), UI reachable on `:8233`.
- This does not change the decision itself — Forge still moves to a real dedicated server once the architecture is validated locally. Treat the local stack as disposable scaffolding for Phase 3/4 validation, not as the target deployment topology.
- Consequence to track: none of the original isolation guarantees (separate network, separate credentials from Actiz) hold during this interim period, since Forge and Actiz's local stacks now share the same machine. Anyone resuming Actiz local development must first bring the `infra` and `actiz-staging` compose projects back up (`docker compose start`).
