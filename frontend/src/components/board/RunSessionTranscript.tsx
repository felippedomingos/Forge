import { useQuery } from '@tanstack/react-query'
import { Wrench } from 'lucide-react'
import { Skeleton } from '@/components/ui/skeleton'
import { api, type TranscriptContentBlock } from '@/lib/api'

// docs/000-Vision.md UC-9's audit/debug angle: the full Claude Code session behind one
// Run, fetched lazily (only mounted while its Run is expanded in TaskDetailSheet) so
// it never slows down the initial timeline/events view.
function TranscriptBlockView({ block }: { block: TranscriptContentBlock }) {
  if (block.type === 'text') {
    return block.text ? <p className="whitespace-pre-wrap">{block.text}</p> : null
  }
  if (block.type === 'tool_use') {
    return (
      <div className="flex items-start gap-1.5 rounded border border-border/60 bg-muted/40 p-1.5">
        <Wrench className="mt-0.5 size-3 shrink-0 text-muted-foreground" />
        <div className="min-w-0">
          <p className="font-medium">{block.toolName}</p>
          {block.toolInput !== null && (
            <pre className="mt-1 overflow-x-auto whitespace-pre-wrap text-[10px] text-muted-foreground">
              {JSON.stringify(block.toolInput, null, 2)}
            </pre>
          )}
        </div>
      </div>
    )
  }
  if (block.type === 'tool_result') {
    return (
      <pre
        className={`overflow-x-auto whitespace-pre-wrap rounded border p-1.5 text-[10px] ${
          block.isError
            ? 'border-destructive/40 bg-destructive/5 text-destructive'
            : 'border-border/60 bg-muted/20 text-muted-foreground'
        }`}
      >
        {block.toolResultText ?? '(empty)'}
      </pre>
    )
  }
  return null
}

export function RunSessionTranscript({ taskId, runId }: { taskId: string; runId: string }) {
  const sessionQuery = useQuery({
    queryKey: ['run-session', taskId, runId],
    queryFn: () => api.getRunSession(taskId, runId),
  })

  if (sessionQuery.isLoading) {
    return (
      <div className="flex flex-col gap-1.5 p-2">
        <Skeleton className="h-3 w-3/4" />
        <Skeleton className="h-3 w-full" />
        <Skeleton className="h-3 w-2/3" />
      </div>
    )
  }

  if (sessionQuery.isError || !sessionQuery.data?.available || sessionQuery.data.messages.length === 0) {
    return (
      <p className="p-2 text-[11px] text-muted-foreground/60">
        Nenhum histórico de sessão disponível para esta execução.
      </p>
    )
  }

  return (
    <ol className="board-scroll flex max-h-72 flex-col gap-2 overflow-y-auto p-2">
      {sessionQuery.data.messages.map((message, i) => (
        <li key={i} className="flex flex-col gap-1 rounded-lg border border-border/60 p-2 text-[11px]">
          <div className="flex items-center gap-2">
            <span className="text-[10px] font-medium tracking-wide text-muted-foreground uppercase">
              {message.role}
            </span>
            {message.timestamp && (
              <span className="text-[10px] text-muted-foreground/60">
                {new Date(message.timestamp).toLocaleTimeString()}
              </span>
            )}
          </div>
          <div className="flex flex-col gap-1.5">
            {message.content.map((block, j) => (
              <TranscriptBlockView key={j} block={block} />
            ))}
          </div>
        </li>
      ))}
    </ol>
  )
}
