import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Circle, DollarSign, ExternalLink } from 'lucide-react'
import { toast } from 'sonner'
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from '@/components/ui/sheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { STATE_CONFIG } from '@/lib/state-config'
import { api, parsePublishRecipe } from '@/lib/api'
import { useTaskWebSocket } from '@/lib/useTaskWebSocket'

// docs/000-Vision.md UC-9: click a task, see what it does and how the agent is
// working right now if it's in progress. The WebSocket (docs/007-ExecutionEngine.md
// §4) is the real, fast path now - it wakes a refetch the instant Postgres NOTIFYs;
// the 10s poll below is just a fallback for a dropped/blocked socket, not the
// primary mechanism anymore.
export function TaskDetailSheet({ taskId, onClose }: { taskId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [answerText, setAnswerText] = useState('')
  const [changesComment, setChangesComment] = useState('')
  const [priorityText, setPriorityText] = useState('')

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
  const events = eventsQuery.data ?? []
  const project = projectsQuery.data?.find((p) => p.id === task?.projectId)
  const previewUrl = parsePublishRecipe(project?.publishRecipe ?? null)?.previewUrl
  const config = task ? STATE_CONFIG[task.state] : null
  const inProgress = task && ['Inbox', 'Executing', 'Publishing'].includes(task.state)
  const totalCost = task?.runs?.reduce((sum, r) => sum + r.costEstimate, 0) ?? 0

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
                  <Button size="sm" variant="outline" disabled={promote.isPending} onClick={() => promote.mutate()}>
                    Promote to Todo →
                  </Button>

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
                  {events.map((e) => (
                    <li key={e.id} className="relative border-l-2 border-border pl-3">
                      <div className="absolute top-1 -left-[5px] size-2 rounded-full bg-primary" />
                      <p className="text-[10px] text-muted-foreground">
                        {new Date(e.occurredAt).toLocaleTimeString()} · {e.actor.replace('agent:', '')}
                      </p>
                      <p className="text-xs">{e.type}</p>
                    </li>
                  ))}
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
                      {task.runs!.map((r) => (
                        <li key={r.id} className="flex justify-between">
                          <span>{r.agentRole}</span>
                          <span>
                            ${r.costEstimate.toFixed(4)} · {r.promptTokens + r.completionTokens} tok
                          </span>
                        </li>
                      ))}
                    </ul>
                    <p className="mt-2 text-right text-xs font-medium">
                      Total: ${totalCost.toFixed(4)}
                    </p>
                  </section>
                </>
              )}
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}
