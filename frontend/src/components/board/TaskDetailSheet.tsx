import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, ChevronDown, ChevronRight, Circle, DollarSign, ExternalLink, GitBranch, X } from 'lucide-react'
import { toast } from 'sonner'
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetFooter,
  SheetTitle,
  SheetDescription,
} from '@/components/ui/sheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { STATE_CONFIG } from '@/lib/state-config'
import { api, parsePublishRecipe, type TaskEvent } from '@/lib/api'
import { getContrastTextColor } from '@/lib/utils'
import { useTaskWebSocket } from '@/lib/useTaskWebSocket'
import { RunSessionTranscript } from './RunSessionTranscript'

const CLARIFICATION_EVENT_TYPES = ['PlannerNeedsClarification', 'DeveloperNeedsClarification']

// AgentActivities.cs's RecordEventAsync payloads are free-form JSON per event type -
// this never throws on malformed/empty strings, it just yields null so callers fall
// back to showing e.type alone (docs/012-API.md TaskEvent.payload).
function parseEventPayload(payload: string): Record<string, unknown> | null {
  try {
    const parsed = JSON.parse(payload)
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : null
  } catch {
    return null
  }
}

// The two shapes *NeedsClarification events actually carry (AgentActivities.cs): a
// real `questions: string[]` from the model, or a `reason: string` operational
// fallback (missing LocalPath, unparseable model response, untrusted project).
function getClarificationQuestions(payload: Record<string, unknown> | null): string[] {
  const questions = payload?.questions
  return Array.isArray(questions) ? questions.filter((q): q is string => typeof q === 'string' && q.length > 0) : []
}

function getClarificationReason(payload: Record<string, unknown> | null): string | null {
  return typeof payload?.reason === 'string' && payload.reason.length > 0 ? payload.reason : null
}

// Timeline summary: best-effort human-readable line from whichever field a given
// event type happens to carry, so the list isn't just a wall of e.type values.
function summarizeEventPayload(event: TaskEvent): string | null {
  const payload = parseEventPayload(event.payload)
  if (!payload) return null

  const questions = getClarificationQuestions(payload)
  if (questions.length === 1) return questions[0]
  if (questions.length > 1) return `${questions.length} questions: ${questions.join(' · ')}`

  const reason = getClarificationReason(payload)
  if (reason) return reason

  for (const key of ['message', 'description', 'worktreePath', 'rawResponse']) {
    const value = payload[key]
    if (typeof value === 'string' && value.length > 0) return value
  }

  return null
}

// docs/000-Vision.md UC-9: click a task, see what it does and how the agent is
// working right now if it's in progress. The WebSocket (docs/007-ExecutionEngine.md
// §4) is the real, fast path now - it wakes a refetch the instant Postgres NOTIFYs;
// the 10s poll below is just a fallback for a dropped/blocked socket, not the
// primary mechanism anymore.
export function TaskDetailSheet({ taskId, onClose }: { taskId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [answerText, setAnswerText] = useState('')
  const [changesComment, setChangesComment] = useState('')
  const [expandedRunId, setExpandedRunId] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [priorityText, setPriorityText] = useState('')

  useEffect(() => {
    setConfirmingDelete(false)
  }, [taskId])

  const taskQuery = useQuery({
    queryKey: ['task', taskId],
    queryFn: () => api.getTask(taskId!),
    enabled: !!taskId,
    refetchInterval: 10000,
  })
  // Same query key/cache as the board's project list (App.tsx) - free here, just a
  // lookup for the task's tag ("{prefix}-{number}").
  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  const eventsQuery = useQuery({
    queryKey: ['task-events', taskId],
    queryFn: () => api.getTaskEvents(taskId!),
    enabled: !!taskId,
    refetchInterval: 10000,
  })

  useTaskWebSocket(taskId, () => {
    queryClient.invalidateQueries({ queryKey: ['task', taskId] })
    queryClient.invalidateQueries({ queryKey: ['task-events', taskId] })
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['tasks'] })
    queryClient.invalidateQueries({ queryKey: ['task', taskId] })
    queryClient.invalidateQueries({ queryKey: ['task-events', taskId] })
  }

  const promote = useMutation({
    mutationFn: () => api.promoteTask(taskId!),
    onSuccess: () => {
      toast.success('Promoted to Todo.')
      invalidate()
    },
  })
  // Founder-requested: send a Backlog task back to Inbox for a fresh Planner pass
  // (e.g. the write-up needs a rewrite before any Developer work starts).
  const requestReplan = useMutation({
    mutationFn: () => api.requestReplan(taskId!),
    onSuccess: () => {
      toast.success('Sent back to Inbox for a fresh plan.')
      invalidate()
    },
    onError: () => toast.error('Could not send the task back to Inbox.'),
  })
  const publish = useMutation({
    mutationFn: () => api.moveTask(taskId!, 'Publishing'),
    onSuccess: () => {
      toast.success('Publishing…')
      invalidate()
    },
  })
  const approve = useMutation({
    mutationFn: () => api.moveTask(taskId!, 'Done'),
    onSuccess: () => {
      toast.success('Approved.')
      invalidate()
    },
  })
  const answer = useMutation({
    mutationFn: () => api.answerTask(taskId!, [answerText]),
    onSuccess: () => {
      toast.success('Answer sent — back to the Planner.')
      setAnswerText('')
      invalidate()
    },
  })
  // docs/004-Workflow.md row 14 - founder-requested: send back for another Developer
  // pass instead of only approving. The comment is what DevelopAsync's next run
  // actually sees (AgentActivities.GetLatestReviewFeedbackAsync).
  const requestChanges = useMutation({
    mutationFn: () => api.requestChanges(taskId!, changesComment),
    onSuccess: () => {
      toast.success('Sent back for another pass.')
      setChangesComment('')
      invalidate()
    },
  })
  // Mirrors ProjectEditDialog's "Delete project": cascades on the backend (sub-tasks,
  // acceptance criteria, runs, events) and best-effort terminates the task's
  // TaskWorkflow. Irreversible, so this requires a second click before firing.
  const deleteTask = useMutation({
    mutationFn: () => api.deleteTask(taskId!),
    onSuccess: () => {
      toast.success('Task deleted.')
      queryClient.invalidateQueries({ queryKey: ['tasks'] })
      onClose()
    },
    onError: () => {
      toast.error('Could not delete the task.')
      setConfirmingDelete(false)
    },
  })
  // Product Owner manual override (docs/000-Vision.md persona) - distinct from the
  // Prioritizer agent's automatic ranking. Only ever called while the task is in
  // Backlog (the section below only renders there); the backend rejects it otherwise.
  const setPriority = useMutation({
    mutationFn: (priority: number) => api.setTaskPriority(taskId!, priority),
    onSuccess: () => {
      toast.success('Priority updated.')
      invalidate()
    },
    onError: () => toast.error('Failed to update priority.'),
  })

  const task = taskQuery.data

  // Founder-requested (docs/013-Frontend.md): pick from the task's own project's
  // existing tags, or create a new one on the fly - both end with the tag attached to
  // this task.
  const [selectedTagId, setSelectedTagId] = useState('')
  const [newTagName, setNewTagName] = useState('')
  const [newTagColor, setNewTagColor] = useState('#3b82f6')

  const projectTagsQuery = useQuery({
    queryKey: ['project-tags', task?.projectId],
    queryFn: () => api.listProjectTags(task!.projectId),
    enabled: !!task,
  })

  const assignTag = useMutation({
    mutationFn: (tagId: string) => api.assignTag(taskId!, tagId),
    onSuccess: () => {
      setSelectedTagId('')
      invalidate()
    },
    onError: () => toast.error('Could not add the tag.'),
  })
  const removeTag = useMutation({
    mutationFn: (tagId: string) => api.removeTag(taskId!, tagId),
    onSuccess: invalidate,
    onError: () => toast.error('Could not remove the tag.'),
  })
  const createTag = useMutation({
    mutationFn: () => api.createTag(task!.projectId, newTagName, newTagColor),
    onSuccess: async (tag) => {
      await api.assignTag(taskId!, tag.id)
      setNewTagName('')
      queryClient.invalidateQueries({ queryKey: ['project-tags', task!.projectId] })
      invalidate()
    },
    onError: () => toast.error('Could not create the tag.'),
  })

  const availableTags = useMemo(() => {
    const assignedIds = new Set((task?.tags ?? []).map((t) => t.id))
    return (projectTagsQuery.data ?? []).filter((t) => !assignedIds.has(t.id))
  }, [projectTagsQuery.data, task?.tags])

  const events = eventsQuery.data ?? []
  const project = projectsQuery.data?.find((p) => p.id === task?.projectId)
  const previewUrl = parsePublishRecipe(project?.publishRecipe ?? null)?.previewUrl
  const config = task ? STATE_CONFIG[task.state] : null
  const inProgress = task && ['Inbox', 'Executing', 'Publishing'].includes(task.state)
  const totalCost = task?.runs?.reduce((sum, r) => sum + r.costEstimate, 0) ?? 0

  const latestClarification = events
    .filter((e) => CLARIFICATION_EVENT_TYPES.includes(e.type))
    .sort((a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime())
    .at(-1)
  const clarificationPayload = latestClarification ? parseEventPayload(latestClarification.payload) : null
  const clarificationQuestions = getClarificationQuestions(clarificationPayload)
  const clarificationReason = getClarificationReason(clarificationPayload)

  // Reset the editable priority field whenever a different task's data lands - only
  // task.priority's own identity (not every unrelated refetch) should clobber
  // whatever the Product Owner is mid-typing.
  useEffect(() => {
    setPriorityText(task?.priority?.toString() ?? '')
  }, [task?.id, task?.priority])

  return (
    <Sheet open={!!taskId} onOpenChange={(open) => !open && onClose()}>
      {/* Founder feedback: fine for this to take up real space while tracking a live
          run - up to half the screen on wide viewports, not the narrow fixed width
          dialogs use. */}
      <SheetContent className="w-full gap-0 overflow-y-auto sm:max-w-[50vw]">
        {!task && (
          <div className="flex flex-col gap-4 p-6">
            <Skeleton className="h-6 w-3/4" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-2/3" />
          </div>
        )}

        {task && config && (
          <>
            <SheetHeader className="gap-1.5 border-b border-border/60 pb-3">
              <div className="flex items-center gap-2">
                <config.icon className={`size-3.5 text-primary ${config.spin ? 'animate-spin' : ''}`} />
                <Badge variant="secondary" className="text-[10px]">{config.label}</Badge>
                {project && (
                  <Badge variant="outline" className="font-mono text-[10px]">
                    {project.prefix}-{task.number}
                  </Badge>
                )}
                {inProgress && (
                  <span className="text-[10px] text-muted-foreground">agent working…</span>
                )}
              </div>
              <SheetTitle className="text-left text-base leading-snug">{task.title}</SheetTitle>
              {task.description && (
                <SheetDescription className="text-left text-xs leading-relaxed text-foreground/80">
                  {task.description}
                </SheetDescription>
              )}
            </SheetHeader>

            <div className="flex flex-col gap-5 px-5 py-4">
              <section>
                <h3 className="mb-2 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                  Tags
                </h3>
                <div className="flex flex-wrap gap-1.5">
                  {(task.tags ?? []).map((tag) => (
                    <Badge
                      key={tag.id}
                      className="gap-1 px-1.5 text-[10px]"
                      style={{ backgroundColor: tag.color, color: getContrastTextColor(tag.color) }}
                    >
                      {tag.name}
                      <button
                        onClick={() => removeTag.mutate(tag.id)}
                        disabled={removeTag.isPending}
                        aria-label={`Remove ${tag.name}`}
                        className="opacity-70 hover:opacity-100"
                      >
                        <X className="size-2.5" />
                      </button>
                    </Badge>
                  ))}
                  {(task.tags?.length ?? 0) === 0 && (
                    <p className="text-xs text-muted-foreground/50">No tags yet.</p>
                  )}
                </div>

                <div className="mt-2 flex items-center gap-1.5">
                  <Select
                    value={selectedTagId}
                    onValueChange={(id) => {
                      setSelectedTagId(id)
                      assignTag.mutate(id)
                    }}
                    disabled={availableTags.length === 0 || assignTag.isPending}
                  >
                    <SelectTrigger size="sm" className="h-7 flex-1 text-xs">
                      <SelectValue
                        placeholder={availableTags.length === 0 ? 'No more tags to add' : 'Add existing tag…'}
                      />
                    </SelectTrigger>
                    <SelectContent>
                      {availableTags.map((tag) => (
                        <SelectItem key={tag.id} value={tag.id}>
                          {tag.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="mt-1.5 flex items-center gap-1.5">
                  <input
                    type="color"
                    value={newTagColor}
                    onChange={(e) => setNewTagColor(e.target.value)}
                    className="size-7 shrink-0 cursor-pointer rounded border border-input bg-transparent"
                    aria-label="New tag color"
                  />
                  <Input
                    className="h-7 flex-1 text-xs"
                    placeholder="New tag name…"
                    value={newTagName}
                    onChange={(e) => setNewTagName(e.target.value)}
                    onKeyDown={(e) => e.key === 'Enter' && newTagName && createTag.mutate()}
                  />
                  <Button
                    size="sm"
                    variant="outline"
                    className="h-7 shrink-0 text-xs"
                    disabled={!newTagName || createTag.isPending}
                    onClick={() => createTag.mutate()}
                  >
                    Create
                  </Button>
                </div>
              </section>

              {task.worktree && (
                <section className="flex flex-col gap-1.5 rounded-lg border border-border/60 bg-muted/30 p-3">
                  <h3 className="flex items-center gap-1.5 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                    <GitBranch className="size-3.5" />
                    Worktree
                  </h3>
                  <p className="font-mono text-xs break-all">{task.worktree.path}</p>
                  <p className="font-mono text-xs text-muted-foreground">{task.worktree.branchName}</p>
                </section>
              )}

              {(task.acceptanceCriteria?.length ?? 0) > 0 && (
                <section>
                  <h3 className="mb-2 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                    Acceptance Criteria
                  </h3>
                  <ul className="flex flex-col gap-1.5">
                    {task.acceptanceCriteria!.map((c) => (
                      <li key={c.id} className="flex items-start gap-2 text-xs">
                        {c.satisfied ? (
                          <CheckCircle2 className="mt-0.5 size-3.5 shrink-0 text-primary" />
                        ) : (
                          <Circle className="mt-0.5 size-3.5 shrink-0 text-muted-foreground/50" />
                        )}
                        <span className={c.satisfied ? 'text-muted-foreground line-through' : ''}>
                          {c.description}
                        </span>
                      </li>
                    ))}
                  </ul>
                </section>
              )}

              {task.state === 'Blocked' && (
                <section className="rounded-lg border border-primary/30 bg-primary/5 p-3">
                  <h3 className="mb-2 text-xs font-medium">The Planner needs an answer</h3>
                  {clarificationQuestions.length > 0 ? (
                    <ul className="mb-2.5 flex list-disc flex-col gap-1 pl-4 text-xs">
                      {clarificationQuestions.map((q, i) => (
                        <li key={i}>{q}</li>
                      ))}
                    </ul>
                  ) : clarificationReason ? (
                    <p className="mb-2.5 text-xs text-muted-foreground">{clarificationReason}</p>
                  ) : null}
                  <div className="flex flex-col gap-2">
                    <Input
                      className="h-8 text-xs"
                      placeholder="Type your answer…"
                      value={answerText}
                      onChange={(e) => setAnswerText(e.target.value)}
                      onKeyDown={(e) => e.key === 'Enter' && answerText && answer.mutate()}
                    />
                    <Button
                      size="sm"
                      disabled={!answerText || answer.isPending}
                      onClick={() => answer.mutate()}
                    >
                      Answer → back to Inbox
                    </Button>
                  </div>
                </section>
              )}

              {task.state === 'Backlog' && (
                <section className="flex flex-col gap-2.5">
                  <div className="flex gap-2">
                    <Button size="sm" variant="outline" disabled={promote.isPending} onClick={() => promote.mutate()}>
                      Promote to Todo →
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-muted-foreground"
                      disabled={requestReplan.isPending}
                      onClick={() => requestReplan.mutate()}
                    >
                      ← Rewrite (back to Inbox)
                    </Button>
                  </div>

                  <div className="flex items-center gap-2">
                    <label className="text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                      Priority
                    </label>
                    <Input
                      type="number"
                      className="h-8 w-20 text-xs"
                      value={priorityText}
                      onChange={(e) => setPriorityText(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key !== 'Enter' || priorityText === '') return
                        setPriority.mutate(Number(priorityText))
                      }}
                    />
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={priorityText === '' || setPriority.isPending}
                      onClick={() => setPriority.mutate(Number(priorityText))}
                    >
                      Set
                    </Button>
                    {task.priorityManuallySet && (
                      <span className="text-[10px] text-muted-foreground">manual override</span>
                    )}
                  </div>
                </section>
              )}
              {task.state === 'AwaitingPublish' && (
                <Button size="sm" disabled={publish.isPending} onClick={() => publish.mutate()}>
                  Publish →
                </Button>
              )}
              {task.state === 'Review' && (
                <section className="flex flex-col gap-2.5">
                  <div className="flex gap-2">
                    {previewUrl && (
                      <Button
                        size="sm"
                        variant="outline"
                        className="gap-1.5"
                        onClick={() => window.open(previewUrl, '_blank', 'noopener,noreferrer')}
                      >
                        <ExternalLink className="size-3.5" />
                        Testar
                      </Button>
                    )}
                    <Button size="sm" disabled={approve.isPending} onClick={() => approve.mutate()}>
                      Approve → Done
                    </Button>
                  </div>

                  {/* Founder-requested: reject a Review-stage task back for another
                      Developer pass with feedback, instead of only approving forward. */}
                  <div className="flex flex-col gap-1.5 rounded-lg border border-dashed border-border/60 p-2.5">
                    <Textarea
                      placeholder="O que funcionou, o que não funcionou, o que ajustar…"
                      value={changesComment}
                      onChange={(e) => setChangesComment(e.target.value)}
                      className="min-h-14 text-xs"
                    />
                    <Button
                      size="sm"
                      variant="secondary"
                      className="w-fit"
                      disabled={!changesComment || requestChanges.isPending}
                      onClick={() => requestChanges.mutate()}
                    >
                      Solicitar ajustes → Todo
                    </Button>
                  </div>
                </section>
              )}

              <Separator />

              <section>
                <h3 className="mb-2.5 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                  Timeline
                </h3>
                <ol className="board-scroll flex max-h-56 flex-col gap-2.5 overflow-y-auto pr-1">
                  {events.map((e) => {
                    const summary = summarizeEventPayload(e)
                    return (
                      <li key={e.id} className="relative border-l-2 border-border pl-3">
                        <div className="absolute top-1 -left-[5px] size-2 rounded-full bg-primary" />
                        <p className="text-[10px] text-muted-foreground">
                          {new Date(e.occurredAt).toLocaleTimeString()} · {e.actor.replace('agent:', '')}
                        </p>
                        <p className="text-xs">{e.type}</p>
                        {summary && <p className="text-[10px] text-muted-foreground">{summary}</p>}
                      </li>
                    )
                  })}
                  {events.length === 0 && (
                    <p className="text-xs text-muted-foreground/60">No events yet.</p>
                  )}
                </ol>
              </section>

              {(task.runs?.length ?? 0) > 0 && (
                <>
                  <Separator />
                  <section>
                    <h3 className="mb-2 flex items-center gap-1.5 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                      <DollarSign className="size-3.5" />
                      Agent runs
                    </h3>
                    <ul className="flex flex-col gap-1 text-[11px] text-muted-foreground">
                      {task.runs!.map((r) => {
                        const isExpanded = expandedRunId === r.id
                        return (
                          <li key={r.id} className="flex flex-col">
                            <button
                              type="button"
                              className="flex items-center justify-between gap-2 rounded px-1 py-0.5 text-left hover:bg-muted/50"
                              onClick={() => setExpandedRunId(isExpanded ? null : r.id)}
                            >
                              <span className="flex items-center gap-1">
                                {isExpanded ? (
                                  <ChevronDown className="size-3 shrink-0" />
                                ) : (
                                  <ChevronRight className="size-3 shrink-0" />
                                )}
                                {r.agentRole}
                              </span>
                              <span>
                                ${r.costEstimate.toFixed(4)} · {r.promptTokens + r.completionTokens} tok
                              </span>
                            </button>
                            {isExpanded && taskId && <RunSessionTranscript taskId={taskId} runId={r.id} />}
                          </li>
                        )
                      })}
                    </ul>
                    <p className="mt-2 text-right text-xs font-medium">
                      Total: ${totalCost.toFixed(4)}
                    </p>
                  </section>
                </>
              )}
            </div>

            <SheetFooter className="border-t border-border/60">
              <Button
                size="sm"
                variant={confirmingDelete ? 'destructive' : 'outline'}
                className="text-muted-foreground hover:text-destructive"
                disabled={deleteTask.isPending}
                onClick={() => (confirmingDelete ? deleteTask.mutate() : setConfirmingDelete(true))}
              >
                {confirmingDelete ? 'Confirmar exclusão' : 'Excluir tarefa'}
              </Button>
            </SheetFooter>
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}
