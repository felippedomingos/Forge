# 011 — Database

## Status

Draft — Phase 3 (Architecture)

## 1. Instance Topology

One PostgreSQL 16 instance, two logical databases (already the shape of `docker/local/docker-compose.yml`):

- `forge` — the schema in this document, managed by EF Core migrations.
- `temporal` / `temporal_visibility` — created and migrated entirely by Temporal's own `auto-setup` tooling ([[ADR-0001]]). Forge's migrations never touch these.

## 2. Schema

Directly implements the entities from [[003-Domain]] §1, plus `agent_memory` (the gap flagged in [[005-Agents]] §7).

```sql
CREATE TABLE projects (
  id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name                  text NOT NULL,
  prefix                text NOT NULL,           -- uppercase, unique; builds each task's tag
                                                   -- ("FORGE-42") - immutable once a task
                                                   -- references it (docs/003-Domain.md §1)
  next_task_number      int  NOT NULL DEFAULT 1,  -- incremented atomically alongside each
                                                   -- new task's insert (docs/012-API.md
                                                   -- POST /tasks)
  repository_url        text NOT NULL,
  root_branch           text NOT NULL,          -- 'main' | 'develop' | 'dev'
  git_provider_plugin_id uuid NOT NULL REFERENCES plugins(id),
  local_path            text NULL,               -- canonical checkout path on the Worker's
                                                   -- machine; added when the real Planner
                                                   -- agent needed something to read
                                                   -- (docs/005-Agents.md §2)
  created_at            timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ix_projects_prefix ON projects(prefix);

CREATE TABLE tasks (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id    uuid NOT NULL REFERENCES projects(id),
  number        int  NOT NULL,                  -- per-project sequential; + Project.prefix
                                                  -- = the task's tag (docs/003-Domain.md §1)
  title         text NOT NULL,
  description   text NULL,
  state         text NOT NULL,                  -- see 003-Domain §3 for the enum values
  priority      int  NULL,
  branch_name   text NULL,
  worktree_id   uuid NULL REFERENCES worktrees(id),
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ix_tasks_project_id_number ON tasks(project_id, number);
CREATE INDEX ix_tasks_project_state    ON tasks(project_id, state);
CREATE INDEX ix_tasks_project_priority ON tasks(project_id, priority);

CREATE TABLE sub_tasks (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id     uuid NOT NULL REFERENCES tasks(id),
  title       text NOT NULL,
  description text NOT NULL,
  order_index int  NOT NULL,
  done        bool NOT NULL DEFAULT false
);

CREATE TABLE acceptance_criteria (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id     uuid NOT NULL REFERENCES tasks(id),
  description text NOT NULL,
  satisfied   bool NOT NULL DEFAULT false
);

CREATE TABLE workers (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name            text NOT NULL,
  status          text NOT NULL,                -- 'idle' | 'busy' | 'offline'
  current_task_id uuid NULL REFERENCES tasks(id),
  home_directory  text NOT NULL,
  created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE worktrees (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id     uuid NOT NULL REFERENCES tasks(id),
  project_id  uuid NOT NULL REFERENCES projects(id),
  path        text NOT NULL,
  branch_name text NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  deleted_at  timestamptz NULL
);
-- INV-2 (003-Domain): at most one ACTIVE worktree per task
CREATE UNIQUE INDEX ux_worktrees_active_task ON worktrees(task_id) WHERE deleted_at IS NULL;

CREATE TABLE runs (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id           uuid NOT NULL REFERENCES tasks(id),
  agent_role        text NOT NULL,               -- Planner|Prioritizer|Developer|Deploy|Git
  model_provider    text NOT NULL,
  started_at        timestamptz NOT NULL DEFAULT now(),
  finished_at       timestamptz NULL,
  status            text NOT NULL,               -- 'success' | 'failed' | 'timeout'
  prompt_tokens     int NOT NULL DEFAULT 0,
  completion_tokens int NOT NULL DEFAULT 0,
  cost_estimate     numeric(10,4) NOT NULL DEFAULT 0
);
CREATE INDEX ix_runs_task ON runs(task_id);

CREATE TABLE events (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id     uuid NULL REFERENCES tasks(id),    -- nullable: some events are system-level
  type        text NOT NULL,                     -- see 003-Domain §4 catalog
  payload     jsonb NOT NULL DEFAULT '{}',
  occurred_at timestamptz NOT NULL DEFAULT now(),
  actor       text NOT NULL                      -- 'user:<id>' | 'agent:<role>'
);
CREATE INDEX ix_events_task_time ON events(task_id, occurred_at);
-- Append-only by convention: no UPDATE/DELETE grants for the application role.

CREATE TABLE plugins (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name          text NOT NULL,
  kind          text NOT NULL,                   -- git_provider|issue_tracker|cloud_cli|database|deployment_target
  version       text NOT NULL,
  configuration jsonb NOT NULL DEFAULT '{}'
);

CREATE TABLE models (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  provider         text NOT NULL,
  capability_tier  text NOT NULL,
  cost_per_token   numeric(12,8) NOT NULL,
  enabled          bool NOT NULL DEFAULT true
);

CREATE TABLE users (
  id    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name  text NOT NULL,
  email text NOT NULL UNIQUE,
  role  text NOT NULL                             -- see 000-Vision §6 personas
);

CREATE TABLE agent_memory (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id uuid NOT NULL REFERENCES projects(id),
  agent_role text NOT NULL,
  key        text NOT NULL,
  value      text NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (project_id, agent_role, key)
);
```

## 3. Event Sourcing Scope

**Decision: not full event sourcing.** `tasks` (and every other domain table) holds current state directly, updated in place by normal CRUD — state is never *reconstructed* by replaying `events`. The `events` table is an append-only audit/timeline log that exists alongside the CRUD tables, feeding the UI's task timeline ([[000-Vision]] UC-9, [[013-Frontend]]) and the audit-trail requirement ([[000-Vision]] §5).

The reason this is enough, rather than needing full event sourcing: Temporal already keeps a complete, durable execution history per task workflow ([[ADR-0001]]) — that *is* the authoritative replay-capable history. Building a second, parallel event-sourced projection on top of `events` would duplicate what Temporal already guarantees, for no real benefit at Forge's current scale. Revisit only if a concrete need emerges for reconstructing state as of an arbitrary past point purely from Postgres, independent of Temporal.

## 4. Migration Strategy

EF Core Migrations against the `forge` database only. `temporal` / `temporal_visibility` are exclusively managed by Temporal's `auto-setup` container ([[ADR-0001]], already working in `docker/local/`) — Forge's migration pipeline must never run against those schemas.

## 5. Multi-Tenancy

**Explicitly out of scope for v1.** Forge is single-tenant: one Postgres instance backs one Forge installation for one team. This is a deliberate non-decision, not an oversight — designing multi-tenancy in now, before Forge has a single real user beyond its own founder, would be exactly the premature abstraction [[000-Vision]] and the founder's own engineering priorities warn against. If Forge is ever offered as a product to other teams, multi-tenancy gets its own ADR at that point, informed by real usage rather than speculation now.
