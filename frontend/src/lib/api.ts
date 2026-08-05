// Mirrors docs/003-Domain.md §3 and docs/012-API.md exactly.
export const TASK_STATES = [
  'Inbox',
  'Backlog',
  'Blocked',
  'Todo',
  'Executing',
  'AwaitingPublish',
  'Publishing',
  'Review',
  'Done',
  'Production',
] as const

export type TaskState = (typeof TASK_STATES)[number]

export interface Project {
  id: string
  name: string
  repositoryUrl: string
  rootBranch: string
  gitProviderPluginId: string
  createdAt: string
}

export interface AcceptanceCriterion {
  id: string
  description: string
  satisfied: boolean
}

export interface TaskEvent {
  id: string
  type: string
  payload: string
  occurredAt: string
  actor: string
}

export interface Run {
  id: string
  agentRole: string
  modelProvider: string
  status: string
  promptTokens: number
  completionTokens: number
  costEstimate: number
  startedAt: string
}

export interface TaskItem {
  id: string
  projectId: string
  title: string
  description: string | null
  state: TaskState
  priority: number | null
  branchName: string | null
  worktreeId: string | null
  createdAt: string
  updatedAt: string
  acceptanceCriteria?: AcceptanceCriterion[]
  runs?: Run[]
}

const BASE_URL = '/api'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (!res.ok) {
    throw new Error(`${init?.method ?? 'GET'} ${path} failed: ${res.status}`)
  }
  return (await res.json()) as T
}

export const api = {
  listProjects: () => request<Project[]>('/projects'),
  listTasks: (projectId?: string) =>
    request<TaskItem[]>(`/tasks${projectId ? `?projectId=${projectId}` : ''}`),
  createTask: (projectId: string, title: string) =>
    request<TaskItem>('/tasks', {
      method: 'POST',
      body: JSON.stringify({ projectId, title }),
    }),
  // docs/006-Scheduler.md §1: stand-in for the BacklogSchedulerWorkflow, which doesn't
  // exist yet - manually promotes one task instead of priority-ordered auto-promotion.
  promoteTask: (taskId: string) =>
    request<void>(`/tasks/${taskId}/promote`, { method: 'POST' }),
  // docs/012-API.md §2 - only the two human-gated transitions this endpoint owns
  // (AwaitingPublish->Publishing, Review->Done). Blocked->Inbox goes through answers().
  moveTask: (taskId: string, targetState: 'Publishing' | 'Done') =>
    request<void>(`/tasks/${taskId}/move`, {
      method: 'POST',
      body: JSON.stringify({ targetState }),
    }),
  answerTask: (taskId: string, answers: string[]) =>
    request<void>(`/tasks/${taskId}/answers`, {
      method: 'POST',
      body: JSON.stringify({ answers }),
    }),
  getTask: (taskId: string) => request<TaskItem>(`/tasks/${taskId}`),
  // docs/000-Vision.md UC-9 - the task detail view's event timeline. Polling stands in
  // for the WebSocket channel docs/007-ExecutionEngine.md §4 still describes as the
  // target mechanism.
  getTaskEvents: (taskId: string) => request<TaskEvent[]>(`/tasks/${taskId}/events`),
}
