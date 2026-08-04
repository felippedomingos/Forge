# Local development stack

Temporary substitute for the dedicated infrastructure in [ADR-0004](../../docs/adr/ADR-0004-dedicated-infrastructure.md), while validating the architecture on Felippe's local machine before provisioning a real server.

## What's here

- **postgres** — Forge's own domain database (`forge` db). Temporal's auto-setup container also creates and migrates its own `temporal` / `temporal_visibility` databases in this same instance.
- **temporal** — Temporal server (auto-setup image runs schema migrations on first boot).
- **temporal-ui** — Web UI at http://localhost:8233 to inspect workflows/activities.

All ports are bound to `127.0.0.1` only — not reachable from the network.

## Usage

```bash
docker compose -f docker-compose.yml up -d      # start
docker compose -f docker-compose.yml ps         # status
docker compose -f docker-compose.yml logs -f    # logs
docker compose -f docker-compose.yml down       # stop (keeps volume/data)
docker compose -f docker-compose.yml down -v    # stop and wipe data
```

Postgres connection string for local development: `Host=localhost;Port=5432;Database=forge;Username=forge;Password=forge_local_dev`

Temporal frontend address for the .NET SDK: `localhost:7233`, namespace `default`.

These are local-only development credentials, not meant to survive the move to the real dedicated server.
