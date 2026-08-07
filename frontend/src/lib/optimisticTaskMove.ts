import type { QueryClient } from '@tanstack/react-query'
import type { TaskItem, TaskState } from './api'

// Founder-reported: every card move (promote, publish, approve, request-changes,
// answer, replan - not just Review->Done, which is where this was first found) had a
// visible delay that read as "didn't work". Root cause: every one of these hits a
// Temporal *signal* (TaskWorkflow.cs's [WorkflowSignal] methods just flip a bool and
// return - RunAsync's own WaitConditionAsync loop is what actually calls SetStateAsync
// and persists the new state, whenever the Worker next resumes that workflow). The
// endpoint's 200 only confirms delivery, not that persistence has happened yet, so a
// refetch immediately after success races ahead and still shows the old state.
//
// Spread this into a useMutation's config for any transition whose trigger only
// exists in one specific source state (so the target is unambiguous and always
// correct once the request actually succeeds) - it patches both the board list and
// the task detail caches the instant the mutation fires, and rolls back if the
// request itself errors. A caller's own onSuccess should still invalidate as before;
// that reconciles with the server's real value once the workflow catches up.
export function optimisticTaskMove<TVars>(
  queryClient: QueryClient,
  getTaskId: (vars: TVars) => string,
  nextState: TaskState,
) {
  return {
    onMutate: async (vars: TVars) => {
      const taskId = getTaskId(vars)
      await queryClient.cancelQueries({ queryKey: ['tasks'] })
      await queryClient.cancelQueries({ queryKey: ['task', taskId] })
      const previousTasks = queryClient.getQueryData<TaskItem[]>(['tasks'])
      const previousTask = queryClient.getQueryData<TaskItem>(['task', taskId])
      queryClient.setQueryData<TaskItem[]>(['tasks'], (old) =>
        old?.map((t) => (t.id === taskId ? { ...t, state: nextState } : t)),
      )
      queryClient.setQueryData<TaskItem>(['task', taskId], (old) => (old ? { ...old, state: nextState } : old))
      return { taskId, previousTasks, previousTask }
    },
    onError: (
      _err: unknown,
      _vars: TVars,
      context?: { taskId: string; previousTasks?: TaskItem[]; previousTask?: TaskItem },
    ) => {
      if (!context) return
      if (context.previousTasks) queryClient.setQueryData(['tasks'], context.previousTasks)
      if (context.previousTask) queryClient.setQueryData(['task', context.taskId], context.previousTask)
    },
  }
}
