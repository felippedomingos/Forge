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
}

export const STATE_CONFIG: Record<TaskState, StateConfig> = {
  Inbox: { label: 'Inbox', icon: Inbox },
  Backlog: { label: 'Backlog', icon: ListTodo, dragTarget: 'Todo' },
  Blocked: { label: 'Blocked', icon: CircleAlert },
  Todo: { label: 'Todo', icon: Circle },
  Executing: { label: 'Executing', icon: Loader2, spin: true },
  AwaitingPublish: { label: 'Awaiting Publish', icon: Send, dragTarget: 'Publishing' },
  Publishing: { label: 'Publishing', icon: UploadCloud, spin: true },
  Review: { label: 'Review', icon: Eye, dragTarget: 'Done' },
  Done: { label: 'Done', icon: CircleCheck },
  Production: { label: 'Production', icon: Rocket },
}

// The reverse map: for a given column, which source state (if any) may be dropped
// into it. Only Todo/Publishing/Done accept a drop, each from exactly one state.
export const DROP_TARGETS: Partial<Record<TaskState, TaskState>> = Object.fromEntries(
  Object.entries(STATE_CONFIG)
    .filter(([, cfg]) => cfg.dragTarget)
    .map(([state, cfg]) => [cfg.dragTarget!, state as TaskState]),
)
