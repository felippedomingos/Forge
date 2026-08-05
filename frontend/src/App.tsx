import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, TASK_STATES, type TaskItem, type TaskState } from './lib/api'

// docs/012-API.md endpoints wired to contextual per-state actions - the only states
// with a human-gated transition (docs/003-Domain.md §3): Blocked (answer), Backlog
// (promote - stand-in for the missing BacklogSchedulerWorkflow, docs/006-Scheduler.md
// §1), AwaitingPublish (publish), Review (approve). Every other state only advances
// on its own via the workflow's own agent activities.
function TaskCard({ task, onOpen }: { task: TaskItem; onOpen: (id: string) => void }) {
  const queryClient = useQueryClient()
  const [answerText, setAnswerText] = useState('')
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['tasks'] })

  const promote = useMutation({ mutationFn: () => api.promoteTask(task.id), onSuccess: invalidate })
  const publish = useMutation({ mutationFn: () => api.moveTask(task.id, 'Publishing'), onSuccess: invalidate })
  const approve = useMutation({ mutationFn: () => api.moveTask(task.id, 'Done'), onSuccess: invalidate })
  const answer = useMutation({
    mutationFn: () => api.answerTask(task.id, [answerText]),
    onSuccess: () => {
      setAnswerText('')
      invalidate()
    },
  })

  // Stop the click from also opening the detail panel when interacting with a
  // control inside the card - only clicking the card body itself opens it.
  const stop = (e: React.SyntheticEvent) => e.stopPropagation()

  return (
    <div
      className="cursor-pointer rounded border border-neutral-200 bg-white p-3 text-sm shadow-sm hover:border-neutral-400 dark:border-neutral-800 dark:bg-neutral-900 dark:hover:border-neutral-600"
      onClick={() => onOpen(task.id)}
    >
      <p className="mb-2">{task.title}</p>

      {task.state === 'Blocked' && (
        <div className="flex flex-col gap-1" onClick={stop}>
          <input
            className="rounded border border-neutral-300 px-2 py-1 text-xs dark:border-neutral-700 dark:bg-neutral-800"
            placeholder="Answer the Planner's question…"
            value={answerText}
            onChange={(e) => setAnswerText(e.target.value)}
          />
          <button
            className="rounded bg-neutral-900 px-2 py-1 text-xs text-white disabled:opacity-40 dark:bg-neutral-100 dark:text-neutral-900"
            disabled={!answerText || answer.isPending}
            onClick={() => answer.mutate()}
          >
            Answer → back to Inbox
          </button>
        </div>
      )}

      {task.state === 'Backlog' && (
        <button
          className="rounded border border-neutral-300 px-2 py-1 text-xs dark:border-neutral-700"
          disabled={promote.isPending}
          onClick={(e) => {
            stop(e)
            promote.mutate()
          }}
        >
          Promote to Todo →
        </button>
      )}

      {task.state === 'AwaitingPublish' && (
        <button
          className="rounded bg-emerald-600 px-2 py-1 text-xs text-white disabled:opacity-40"
          disabled={publish.isPending}
          onClick={(e) => {
            stop(e)
            publish.mutate()
          }}
        >
          Publish →
        </button>
      )}

      {task.state === 'Review' && (
        <button
          className="rounded bg-emerald-600 px-2 py-1 text-xs text-white disabled:opacity-40"
          disabled={approve.isPending}
          onClick={(e) => {
            stop(e)
            approve.mutate()
          }}
        >
          Approve → Done
        </button>
      )}
    </div>
  )
}

// docs/000-Vision.md UC-9: click a task, see what it does and what the agent is doing
// right now if it's in progress. Polls the task + its event timeline every 2s while
// open - a stand-in for the WebSocket channel docs/007-ExecutionEngine.md §4 describes
// as the target, same pattern as the board's own polling.
function TaskDetailPanel({ taskId, onClose }: { taskId: string; onClose: () => void }) {
  const taskQuery = useQuery({
    queryKey: ['task', taskId],
    queryFn: () => api.getTask(taskId),
    refetchInterval: 2000,
  })
  const eventsQuery = useQuery({
    queryKey: ['task-events', taskId],
    queryFn: () => api.getTaskEvents(taskId),
    refetchInterval: 2000,
  })

  const task = taskQuery.data
  const events = eventsQuery.data ?? []
  const inProgress = task && !['Blocked', 'AwaitingPublish', 'Review', 'Done', 'Production'].includes(task.state)

  return (
    <div className="fixed inset-0 z-10 flex justify-end bg-black/20" onClick={onClose}>
      <div
        className="h-full w-full max-w-md overflow-y-auto bg-white p-6 shadow-xl dark:bg-neutral-900"
        onClick={(e) => e.stopPropagation()}
      >
        <button className="mb-4 text-sm text-neutral-500" onClick={onClose}>
          ← Close
        </button>

        {!task && <p className="text-sm text-neutral-500">Loading…</p>}

        {task && (
          <>
            <h2 className="text-lg font-semibold">{task.title}</h2>
            <p className="mt-1 text-sm text-neutral-500">
              {task.state}
              {inProgress && ' · agent working…'}
            </p>

            {task.description && (
              <p className="mt-4 text-sm">{task.description}</p>
            )}

            {(task.acceptanceCriteria?.length ?? 0) > 0 && (
              <div className="mt-4">
                <h3 className="mb-2 text-xs font-medium uppercase text-neutral-500">
                  Acceptance Criteria
                </h3>
                <ul className="flex flex-col gap-1 text-sm">
                  {task.acceptanceCriteria!.map((c) => (
                    <li key={c.id} className="flex gap-2">
                      <span>{c.satisfied ? '✓' : '○'}</span>
                      <span>{c.description}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <div className="mt-4">
              <h3 className="mb-2 text-xs font-medium uppercase text-neutral-500">
                Timeline
              </h3>
              <ul className="flex flex-col gap-2 text-xs">
                {events.map((e) => (
                  <li key={e.id} className="border-l-2 border-neutral-200 pl-2 dark:border-neutral-700">
                    <div className="text-neutral-400">
                      {new Date(e.occurredAt).toLocaleTimeString()} · {e.actor}
                    </div>
                    <div>{e.type}</div>
                  </li>
                ))}
                {events.length === 0 && <li className="text-neutral-400">No events yet.</li>}
              </ul>
            </div>

            {(task.runs?.length ?? 0) > 0 && (
              <div className="mt-4">
                <h3 className="mb-2 text-xs font-medium uppercase text-neutral-500">
                  Agent Runs (cost)
                </h3>
                <ul className="flex flex-col gap-1 text-xs">
                  {task.runs!.map((r) => (
                    <li key={r.id}>
                      {r.agentRole} · {r.status} · ${r.costEstimate.toFixed(4)} ·{' '}
                      {r.promptTokens + r.completionTokens} tokens
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
}

// First slice of docs/013-Frontend.md's Kanban board (docs/000-Vision.md §9 states).
// Cross-project view (UC-1): no project filter applied by default.
function Board() {
  const queryClient = useQueryClient()
  const [newTitle, setNewTitle] = useState('')
  const [selectedProjectId, setSelectedProjectId] = useState<string>('')
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)

  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  // 2s polling stands in for the real WebSocket trace (docs/007-ExecutionEngine.md §4)
  // so automatic transitions (Inbox->Backlog, etc, driven by the workflow's own agent
  // activities, not a human click) show up without a manual refresh.
  const tasksQuery = useQuery({
    queryKey: ['tasks'],
    queryFn: () => api.listTasks(),
    refetchInterval: 2000,
  })

  const createTask = useMutation({
    mutationFn: () => api.createTask(selectedProjectId, newTitle),
    onSuccess: () => {
      setNewTitle('')
      queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
  })

  const projects = projectsQuery.data ?? []
  const tasks = tasksQuery.data ?? []

  const tasksByState = (state: TaskState) => tasks.filter((t) => t.state === state)

  return (
    <div className="min-h-screen bg-neutral-50 text-neutral-900 dark:bg-neutral-950 dark:text-neutral-100">
      <header className="border-b border-neutral-200 px-6 py-4 dark:border-neutral-800">
        <h1 className="text-xl font-semibold">Forge</h1>
        <p className="text-sm text-neutral-500">
          {projects.length} project{projects.length === 1 ? '' : 's'} · {tasks.length} task
          {tasks.length === 1 ? '' : 's'}
        </p>
      </header>

      <section className="flex gap-2 border-b border-neutral-200 px-6 py-3 dark:border-neutral-800">
        <select
          className="rounded border border-neutral-300 bg-white px-2 py-1 text-sm dark:border-neutral-700 dark:bg-neutral-900"
          value={selectedProjectId}
          onChange={(e) => setSelectedProjectId(e.target.value)}
        >
          <option value="">Select a project…</option>
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <input
          className="flex-1 rounded border border-neutral-300 bg-white px-2 py-1 text-sm dark:border-neutral-700 dark:bg-neutral-900"
          placeholder="New task title — docs/000-Vision.md UC-3"
          value={newTitle}
          onChange={(e) => setNewTitle(e.target.value)}
        />
        <button
          className="rounded bg-neutral-900 px-3 py-1 text-sm text-white disabled:opacity-40 dark:bg-neutral-100 dark:text-neutral-900"
          disabled={!selectedProjectId || !newTitle || createTask.isPending}
          onClick={() => createTask.mutate()}
        >
          Create
        </button>
      </section>

      {projects.length === 0 && !projectsQuery.isLoading && (
        <p className="px-6 py-4 text-sm text-neutral-500">
          No projects yet — create one via <code>POST /api/projects</code> (no UI for this
          yet, it needs a Git provider plugin configured first, see docs/010-Plugins.md).
        </p>
      )}

      <div className="flex gap-4 overflow-x-auto p-6">
        {TASK_STATES.map((state) => (
          <div key={state} className="w-64 shrink-0">
            <h2 className="mb-2 text-sm font-medium text-neutral-500">
              {state} ({tasksByState(state).length})
            </h2>
            <div className="flex flex-col gap-2">
              {tasksByState(state).map((task) => (
                <TaskCard key={task.id} task={task} onOpen={setOpenTaskId} />
              ))}
            </div>
          </div>
        ))}
      </div>

      {openTaskId && <TaskDetailPanel taskId={openTaskId} onClose={() => setOpenTaskId(null)} />}
    </div>
  )
}

export default function App() {
  return <Board />
}
