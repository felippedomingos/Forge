import { useEffect, useRef } from 'react'

// docs/007-ExecutionEngine.md §4 - the real WebSocket trace channel. Connects to
// /api/ws/tasks/{id} (proxied to Forge.Api's WebSocket endpoint, docs/012-API.md §3)
// and calls onMessage whenever the server pushes a "refresh" signal (Postgres NOTIFY
// picked up by PostgresNotificationListener). The message itself carries no data -
// the caller re-fetches over REST, matching TaskEventBroadcaster's design.
//
// onMessage is read via a ref rather than listed as an effect dependency, so a new
// (inline) callback on every render doesn't reconnect the socket - only a real
// taskId change should do that.
export function useTaskWebSocket(taskId: string | null, onMessage: () => void) {
  const onMessageRef = useRef(onMessage)
  onMessageRef.current = onMessage

  useEffect(() => {
    if (!taskId) return

    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
    const socket = new WebSocket(`${protocol}//${location.host}/api/ws/tasks/${taskId}`)
    socket.onmessage = () => onMessageRef.current()

    return () => socket.close()
  }, [taskId])
}
