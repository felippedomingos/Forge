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
}
