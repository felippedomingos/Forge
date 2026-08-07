import type { TaskState } from './api'

// Founder-reported: every card move (promote, publish, approve, request-changes,
// answer, replan) had a visible delay that read as "didn't work" - see
// useOptimisticTaskStates.ts for the full root cause (a Temporal signal's HTTP 200
// only confirms *delivery*, not that TaskWorkflow.cs has processed it and persisted
// the real state yet) and why the override lives outside the query cache (immune to
// being clobbered by an in-flight poll).
//
// Spread this into a useMutation's config for any transition whose trigger only
// exists in one specific source state, so the target is unambiguous and always
// correct once the request actually succeeds.
export function optimisticTaskMove<TVars>(
  setOverride: (taskId: string, state: TaskState) => void,
  clearOverride: (taskId: string) => void,
  getTaskId: (vars: TVars) => string,
  nextState: TaskState,
) {
  return {
    onMutate: (vars: TVars) => {
      const taskId = getTaskId(vars)
      setOverride(taskId, nextState)
      return { taskId }
    },
    onError: (_err: unknown, _vars: TVars, context?: { taskId: string }) => {
      if (context) clearOverride(context.taskId)
    },
  }
}
