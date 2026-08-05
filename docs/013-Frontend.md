# 013 — Frontend

## Status

Draft — Phase 4 (Implementation), first slice built

## 1. Stack

Vite + React + TypeScript + Tailwind v4 (via the `@tailwindcss/vite` plugin, not a separate PostCSS config) + TanStack Query for server state. No client-side router yet — a single-page Kanban board is the entire UI at this stage, matching what's actually in `frontend/` today rather than describing a larger app that doesn't exist.

## 2. What's Built (First Slice)

- **Board**: all 10 states from [[003-Domain]] §3 rendered as columns, populated from `GET /tasks` ([[012-API]]).
- **Quick-add**: a project selector + title input, hitting `POST /tasks` per [[000-Vision]] UC-3.
- **Contextual per-card actions**, matching exactly the human-gated transitions in [[003-Domain]] §3 — no button exists for a transition that isn't human-gated:
  - `Blocked` → a text input + "Answer" button (`POST /tasks/{id}/answers`)
  - `Backlog` → "Promote to Todo" (`POST /tasks/{id}/promote` — a stand-in for the missing `BacklogSchedulerWorkflow`, [[006-Scheduler]] §1)
  - `AwaitingPublish` → "Publish" (`POST /tasks/{id}/move`, target `Publishing`)
  - `Review` → "Approve → Done" (`POST /tasks/{id}/move`, target `Done`)
- **Live-ish updates**: the task list polls every 2 seconds (`refetchInterval`) so automatic transitions (e.g. `Inbox → Backlog` the instant the Planner activity completes) show up without a manual refresh.

## 3. What's Explicitly Not Built Yet

- **Navigation / other views**: Projects list, Execution view, Logs, Metrics, Models, Workers, Settings — none exist. Today's app is the Kanban board and nothing else.
- **Task detail view**: no per-task page showing description, acceptance criteria, sub-tasks, diff, commits, or the live agent trace from [[000-Vision]] UC-9. Clicking a card does nothing today.
- **Real-time via WebSocket**: 2-second polling is a deliberate stand-in, not the target mechanism. [[007-ExecutionEngine]] §4 already specifies the real design (a WebSocket channel per task, fed by the agent activity's trace calls) — implementing it requires the activities to actually produce a trace, which they don't yet since [[005-Agents]]'s roles are still stubs.
- **Board views beyond Kanban** (list, timeline, tree) — post-v1 per [[000-Vision]] §7's note that Kanban is one view among several eventually.

## 4. Design Notes

- No design system / component library chosen yet — Tailwind utility classes directly, consistent with keeping the skeleton's footprint small until there's enough UI surface to justify extracting shared components.
- Dark mode support exists via Tailwind's `dark:` variants (system preference), not a user toggle.
- The Vite dev server proxies `/api` to `http://localhost:5080` ([[012-API]]) so no absolute URL is hardcoded anywhere in the frontend — the same build works against any host serving the API at the same relative path.

## 5. Open Questions

- Once the task detail view exists, does it live at a real route (requiring a router) or as a modal/drawer over the board? Not decided — depends on how much state (execution trace, diff viewer) it needs to hold, which isn't known until [[007-ExecutionEngine]]'s WebSocket channel is real.
- Whether polling should stay as a fallback even after WebSocket exists (e.g. for clients that lose the socket) — reasonable, not designed yet.
