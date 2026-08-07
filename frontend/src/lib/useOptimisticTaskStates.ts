import { useCallback, useEffect, useRef, useState } from 'react'
import type { TaskState } from './api'

// Founder-reported, second pass: card moves still looked like they "didn't apply"
// even after lib/optimisticTaskMove.ts started patching the query cache directly in
// onMutate. Root cause: `tasksQuery` (App.tsx) polls every 2s and `taskQuery`
// (TaskDetailSheet.tsx) every 10s, independently of any mutation - if either poll
// fires before TaskWorkflow.cs's async signal-processing has actually landed on the
// server, it fetches the OLD state and overwrites the cache patch with
// `setQueryData`, so the card would flash to the new column and immediately snap back
// until the real transition eventually landed (which, from the user's side, looked
// exactly like "still not applying").
//
// Keeping the override in its own state (not the query cache) and merging it in at
// render time makes it immune to any poll clobbering it - a background refetch
// updates the query cache same as always, but the merge re-applies the override on
// top every render. `reconcile` is how an override actually clears: called with a
// query's freshly-fetched real state, it only removes the override once the server
// genuinely agrees. A timeout is the fallback for a transition that never lands the
// way expected (a rejected/failed signal) - the UI shouldn't lie forever.
const OVERRIDE_TIMEOUT_MS = 20000

export function useOptimisticTaskStates() {
  const [overrides, setOverrides] = useState<Record<string, TaskState>>({})
  const timeoutsRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})

  const clearOverride = useCallback((taskId: string) => {
    clearTimeout(timeoutsRef.current[taskId])
    delete timeoutsRef.current[taskId]
    setOverrides((prev) => {
      if (!(taskId in prev)) return prev
      const next = { ...prev }
      delete next[taskId]
      return next
    })
  }, [])

  const setOverride = useCallback(
    (taskId: string, state: TaskState) => {
      clearTimeout(timeoutsRef.current[taskId])
      timeoutsRef.current[taskId] = setTimeout(() => clearOverride(taskId), OVERRIDE_TIMEOUT_MS)
      setOverrides((prev) => ({ ...prev, [taskId]: state }))
    },
    [clearOverride],
  )

  // Call whenever a query returns fresh data for a task - clears the override the
  // instant the server confirms it, rather than waiting for the timeout.
  const reconcile = useCallback((taskId: string, realState: TaskState) => {
    setOverrides((prev) => {
      if (prev[taskId] !== realState) return prev
      clearTimeout(timeoutsRef.current[taskId])
      delete timeoutsRef.current[taskId]
      const next = { ...prev }
      delete next[taskId]
      return next
    })
  }, [])

  useEffect(() => {
    const timeouts = timeoutsRef.current
    return () => {
      Object.values(timeouts).forEach(clearTimeout)
    }
  }, [])

  return { overrides, setOverride, clearOverride, reconcile }
}
