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
- **Task detail panel** (founder-requested, [[000-Vision]] UC-9): clicking a card opens a slide-over panel with description, an acceptance-criteria checklist, a live event timeline, and per-run cost — all polling every 2s. This is what actually shows "the agent is working" while a task is mid-`Inbox`/`Executing`/etc: the timeline shows `PlannerStarted` → `PlannerInvokingModel` → `PlannerCompleted` in near-real-time during a real (multi-second) Claude call.

## 3. What's Explicitly Not Built Yet

- **Navigation / other views**: Projects list, Execution view, Logs, Metrics, Models, Workers, Settings — none exist. Today's app is the Kanban board (plus the task detail panel) and nothing else.
- **Diff/commits view**: the task detail panel shows description/criteria/events/cost, not a code diff or commit list — meaningless until the Developer/Git agents actually touch code.
- **Real-time via WebSocket**: 2-second polling is a deliberate stand-in, not the target mechanism, for both the board and the task detail panel. [[007-ExecutionEngine]] §4 already specifies the real design (a WebSocket channel per task) — the activities now produce real trace events (via the `events` table), so a WebSocket layer could be added without changing what data exists, only how it's delivered.
- **Board views beyond Kanban** (list, timeline, tree) — post-v1 per [[000-Vision]] §7's note that Kanban is one view among several eventually.
- **Visual design**: the founder has flagged visual/design concerns to revisit later — current styling is deliberately minimal (plain Tailwind utilities, no design system) and not treated as final.

## 4. Design Notes

- No design system / component library chosen yet — Tailwind utility classes directly, consistent with keeping the skeleton's footprint small until there's enough UI surface to justify extracting shared components.
- Dark mode support exists via Tailwind's `dark:` variants (system preference), not a user toggle.
- The Vite dev server proxies `/api` to `http://localhost:5080` ([[012-API]]) so no absolute URL is hardcoded anywhere in the frontend — the same build works against any host serving the API at the same relative path.

## 5. Open Questions

- The task detail panel is a client-side overlay (no router, no real route/URL) — fine for now, revisit if deep-linking to a specific task becomes a real need.
- Whether polling should stay as a fallback even after WebSocket exists (e.g. for clients that lose the socket) — reasonable, not designed yet.
