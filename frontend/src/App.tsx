import { useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { Flame, Tag as TagIcon } from 'lucide-react'
import { Toaster, toast } from 'sonner'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { CreateTaskDialog } from '@/components/board/CreateTaskDialog'
import { BoardColumn } from '@/components/board/BoardColumn'
import { TaskCardOverlay } from '@/components/board/TaskCard'
import { TaskDetailSheet } from '@/components/board/TaskDetailSheet'
import { ProjectSidebar } from '@/components/board/ProjectSidebar'
import { LoginScreen } from '@/components/auth/LoginScreen'
import { api, TASK_STATES, type TaskState } from '@/lib/api'
import { DROP_TARGETS } from '@/lib/state-config'
import { AUTH_INVALID_EVENT, getCurrentUser, type AuthUser } from '@/lib/auth'

// docs/013-Frontend.md: first slice of the board (docs/000-Vision.md §9 states).
// Cross-project view (UC-1) by default; a project filter narrows it.
function Board({ user }: { user: AuthUser }) {
  const queryClient = useQueryClient()
  const [selectedProjectId, setSelectedProjectId] = useState<string>('all')
  // Founder-requested board filter by Tag (docs/013-Frontend.md) - reset whenever the
  // project filter changes, since a tag from another project would otherwise stay
  // selected while matching nothing.
  const [selectedTagId, setSelectedTagId] = useState<string>('all')
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)
  const [draggingFromState, setDraggingFromState] = useState<TaskState | null>(null)
  // The id of the task currently being dragged, if any - drives <DragOverlay> below
  // (docs: overlay renders outside BoardColumn's clipped, overflow-y-auto container
  // so the card stays visible while dragged across columns instead of being clipped).
  const [activeTaskId, setActiveTaskId] = useState<string | null>(null)
  // Founder-reported: drag-and-drop wasn't working. Root cause: the whole card had no
  // drag listeners at all - only a tiny, hover-only grip icon did, and it reserved
  // inconsistent layout space besides (see TaskCard.tsx). Moving listeners to the
  // whole card requires an activation distance here, or a plain click-to-open would
  // misfire as a drag on the slightest pointer jitter - 8px lets dnd-kit tell a real
  // drag gesture from a click before either fires.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 8 } }))

  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  // 2s polling stands in for the WebSocket trace (docs/007-ExecutionEngine.md §4) so
  // automatic transitions (driven by agent activities, not a human click) show up
  // without a manual refresh.
  const tasksQuery = useQuery({
    queryKey: ['tasks'],
    queryFn: () => api.listTasks(),
    refetchInterval: 2000,
  })

  const invalidateTasks = () => queryClient.invalidateQueries({ queryKey: ['tasks'] })
  const promote = useMutation({ mutationFn: (id: string) => api.promoteTask(id), onSuccess: invalidateTasks })
  const publish = useMutation({ mutationFn: (id: string) => api.moveTask(id, 'Publishing'), onSuccess: invalidateTasks })
  const approve = useMutation({ mutationFn: (id: string) => api.moveTask(id, 'Done'), onSuccess: invalidateTasks })

  const projects = projectsQuery.data ?? []
  const allTasks = tasksQuery.data ?? []
  const tasksInProject =
    selectedProjectId === 'all' ? allTasks : allTasks.filter((t) => t.projectId === selectedProjectId)
  // Filter options are derived from the tasks actually in view, not a separate
  // per-project tags fetch - a tag with zero tasks on the board isn't worth listing here.
  const availableTags = useMemo(() => {
    const byId = new Map(tasksInProject.flatMap((t) => t.tags ?? []).map((tag) => [tag.id, tag]))
    return Array.from(byId.values()).sort((a, b) => a.name.localeCompare(b.name))
  }, [tasksInProject])
  const tasks =
    selectedTagId === 'all'
      ? tasksInProject
      : tasksInProject.filter((t) => t.tags?.some((tag) => tag.id === selectedTagId))

  const prefixByProjectId = useMemo(
    () => Object.fromEntries(projects.map((p) => [p.id, p.prefix])),
    [projects],
  )
  const colorByProjectId = useMemo(
    () => Object.fromEntries(projects.map((p) => [p.id, p.color])),
    [projects],
  )
  const activeTasks = useMemo(() => allTasks.filter((t) => t.state !== 'Production'), [allTasks])
  const taskCountByProject = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const t of activeTasks) counts[t.projectId] = (counts[t.projectId] ?? 0) + 1
    return counts
  }, [activeTasks])

  const handleDragStart = (event: DragStartEvent) => {
    setDraggingFromState((event.active.data.current?.state as TaskState) ?? null)
    setActiveTaskId(event.active.id as string)
  }

  const handleDragEnd = (event: DragEndEvent) => {
    setDraggingFromState(null)
    setActiveTaskId(null)
    const sourceState = event.active.data.current?.state as TaskState | undefined
    const targetState = event.over?.id as TaskState | undefined
    const taskId = event.active.id as string
    if (!sourceState || !targetState) return

    if (DROP_TARGETS[targetState] !== sourceState) {
      toast.error(`Can't move a task directly from ${sourceState} to ${targetState}.`)
      return
    }

    if (sourceState === 'Backlog') promote.mutate(taskId)
    else if (sourceState === 'AwaitingPublish') publish.mutate(taskId)
    else if (sourceState === 'Review') approve.mutate(taskId)
  }

  const handleDragCancel = () => {
    setDraggingFromState(null)
    setActiveTaskId(null)
  }

  const activeTask = activeTaskId ? tasks.find((t) => t.id === activeTaskId) : undefined

  const selectedProject = projects.find((p) => p.id === selectedProjectId)

  return (
    <div className="flex h-screen overflow-hidden bg-background text-foreground">
      <Toaster theme="dark" position="bottom-right" />

      <ProjectSidebar
        projects={projects}
        selectedProjectId={selectedProjectId}
        onSelectProject={(id) => {
          setSelectedProjectId(id)
          setSelectedTagId('all')
        }}
        taskCountByProject={taskCountByProject}
        totalTaskCount={activeTasks.length}
        user={user}
      />

      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        <header className="flex items-center gap-3 border-b border-border/60 px-4 py-2.5">
          <h2 className="text-sm font-medium">
            {selectedProjectId === 'all' ? 'All tasks' : (selectedProject?.name ?? '…')}
          </h2>

          <p className="text-xs text-muted-foreground">
            {tasks.length} task{tasks.length === 1 ? '' : 's'}
          </p>

          {availableTags.length > 0 && (
            <Select value={selectedTagId} onValueChange={setSelectedTagId}>
              <SelectTrigger size="sm" className="h-7 gap-1.5 text-xs">
                <TagIcon className="size-3.5 text-muted-foreground" />
                <SelectValue placeholder="All tags" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All tags</SelectItem>
                {availableTags.map((tag) => (
                  <SelectItem key={tag.id} value={tag.id}>
                    {tag.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}

          <div className="ml-auto">
            <CreateTaskDialog projects={projects} />
          </div>
        </header>

        {projectsQuery.isLoading ? (
          <div className="grid flex-1 grid-cols-10 gap-2 p-4">
            {Array.from({ length: 10 }).map((_, i) => (
              <Skeleton key={i} className="rounded-lg" />
            ))}
          </div>
        ) : projects.length === 0 ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-2 text-center">
            <Flame className="size-8 text-muted-foreground/40" />
            <p className="text-sm text-muted-foreground">
              No projects yet — create one via <code className="rounded bg-muted px-1">POST /api/projects</code>.
            </p>
          </div>
        ) : (
          <DndContext
            sensors={sensors}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
            onDragCancel={handleDragCancel}
          >
            {/* Founder feedback: all 10 columns fit on screen, no horizontal scroll -
                an even grid instead of a scrolling flex row. */}
            <div className="grid flex-1 grid-cols-10 gap-2 overflow-hidden p-4">
              {TASK_STATES.map((state) => (
                <BoardColumn
                  key={state}
                  state={state}
                  tasks={tasks.filter((t) => t.state === state)}
                  onOpenTask={setOpenTaskId}
                  draggingFromState={draggingFromState}
                  prefixByProjectId={prefixByProjectId}
                  colorByProjectId={colorByProjectId}
                />
              ))}
            </div>
            {/* Founder-reported: dragging felt like holding an empty space and drops
                were imprecise. Root cause: without a DragOverlay, the dragged
                TaskCard was translated in place inside BoardColumn's
                overflow-y-auto container - CSS clips overflow-x too once
                overflow-y is set, so the card visually vanished as soon as it
                crossed its origin column's horizontal bounds. Rendering the
                active card here instead keeps it above every column, unclipped,
                for the whole gesture. */}
            <DragOverlay>
              {activeTask ? (
                <TaskCardOverlay
                  task={activeTask}
                  tag={`${prefixByProjectId[activeTask.projectId] ?? '?'}-${activeTask.number}`}
                  color={colorByProjectId[activeTask.projectId] ?? '#E5E7EB'}
                />
              ) : null}
            </DragOverlay>
          </DndContext>
        )}
      </div>

      <TaskDetailSheet taskId={openTaskId} onClose={() => setOpenTaskId(null)} />
    </div>
  )
}

// docs/adr/ADR-0006 - gates the whole app behind a valid, unexpired JWT. Re-checks on
// AUTH_INVALID_EVENT (api.ts's request() fires it on any 401 - token expired mid-
// session, not just at load) so a stale token drops back to the login screen instead
// of leaving the board silently broken.
export default function App() {
  const [user, setUser] = useState<AuthUser | null>(() => getCurrentUser())

  useEffect(() => {
    const onAuthInvalid = () => setUser(null)
    window.addEventListener(AUTH_INVALID_EVENT, onAuthInvalid)
    return () => window.removeEventListener(AUTH_INVALID_EVENT, onAuthInvalid)
  }, [])

  if (!user) {
    return <LoginScreen onAuthenticated={() => setUser(getCurrentUser())} />
  }

  return <Board user={user} />
}
