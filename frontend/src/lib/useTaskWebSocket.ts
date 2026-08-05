import { useEffect, useRef } from 'react'
import { getToken } from './auth'

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

    // docs/adr/ADR-0006 - the browser's native WebSocket API can't set a custom
    // Authorization header, so the token rides along as a query param instead
    // (extracted server-side in Program.cs's JwtBearerEvents.OnMessageReceived).
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
    const token = getToken()
    const socket = new WebSocket(
      `${protocol}//${location.host}/api/ws/tasks/${taskId}${token ? `?access_token=${encodeURIComponent(token)}` : ''}`,
    )
    socket.onmessage = () => onMessageRef.current()

    return () => socket.close()
  }, [taskId])
}
