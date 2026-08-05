import {
  Inbox,
  ListTodo,
  CircleAlert,
  Circle,
  Loader2,
  Send,
  UploadCloud,
  Eye,
  CircleCheck,
  Rocket,
  type LucideIcon,
} from 'lucide-react'
import type { TaskState } from './api'

export interface StateConfig {
  label: string
  icon: LucideIcon
  spin?: boolean
  // docs/003-Domain.md §3: only these 3 states have a human-gated forward action a
  // drag gesture can represent. Every other state advances on its own via the
  // workflow's agent activities - dragging from there wouldn't mean anything valid.
  dragTarget?: TaskState
  // Founder-requested: a short, always-visible explanation of what happens to a
  // task in (or moved into) this column - so the board is self-explanatory to
  // anyone looking at it, not just whoever built it.
  hint: string
}

export const STATE_CONFIG: Record<TaskState, StateConfig> = {
  Inbox: { label: 'Inbox', icon: Inbox, hint: 'Planner reads the repo & plans it' },
  Backlog: { label: 'Backlog', icon: ListTodo, dragTarget: 'Todo', hint: 'Auto-promotes when a worker is free' },
  Blocked: { label: 'Blocked', icon: CircleAlert, hint: 'Needs your answer to continue' },
  Todo: { label: 'Todo', icon: Circle, hint: 'Waiting for a free worker slot' },
  Executing: { label: 'Executing', icon: Loader2, spin: true, hint: 'Developer agent is writing code' },
  AwaitingPublish: { label: 'Awaiting Publish', icon: Send, dragTarget: 'Publishing', hint: 'Drag/click Publish when ready' },
  Publishing: { label: 'Publishing', icon: UploadCloud, spin: true, hint: 'Deploy agent is running' },
  Review: { label: 'Review', icon: Eye, dragTarget: 'Done', hint: 'Test it, then approve' },
  Done: { label: 'Done', icon: CircleCheck, hint: 'Git agent pushes branch & opens PR' },
  Production: { label: 'Production', icon: Rocket, hint: 'Pipeline confirmed deployment' },
}

// The reverse map: for a given column, which source state (if any) may be dropped
// into it. Only Todo/Publishing/Done accept a drop, each from exactly one state.
export const DROP_TARGETS: Partial<Record<TaskState, TaskState>> = Object.fromEntries(
  Object.entries(STATE_CONFIG)
    .filter(([, cfg]) => cfg.dragTarget)
    .map(([state, cfg]) => [cfg.dragTarget!, state as TaskState]),
)
