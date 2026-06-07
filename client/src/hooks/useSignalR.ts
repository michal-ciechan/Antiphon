import { useEffect, useRef } from 'react'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { useConnectionStore } from '../stores/connectionStore'

const SIGNALR_HUB_URL = '/hubs/antiphon'
const RETRY_INTERVAL_MS = 5000

function createConnection() {
  return new HubConnectionBuilder()
    .withUrl(SIGNALR_HUB_URL)
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()
}

/**
 * Manages the SignalR connection lifecycle (connect, reconnect, disconnect).
 * Uses withAutomaticReconnect() for resilience and updates connectionStore status.
 * After all automatic reconnect attempts are exhausted, keeps retrying every 5s
 * so the connection recovers after a server restart without requiring a page refresh.
 */
export function useSignalR() {
  const connectionRef = useRef<HubConnection | null>(null)
  const setStatus = useConnectionStore((s) => s.setStatus)

  if (connectionRef.current === null) {
    connectionRef.current = createConnection()
  }

  useEffect(() => {
    // Per-effect-run cancellation flag. Using a local (not a shared ref) means a
    // stale promise resolving after StrictMode's unmount/remount can't touch the
    // live run — it only sees its own run's `cancelled`.
    let cancelled = false
    let retryTimer: ReturnType<typeof setTimeout> | null = null
    const connection = connectionRef.current!

    const tryStart = () => {
      if (cancelled) return
      // StrictMode may remount while a prior stop() is still in flight. start()
      // throws unless the connection is fully Disconnected, so wait it out.
      if (connection.state !== HubConnectionState.Disconnected) {
        retryTimer = setTimeout(tryStart, 100)
        return
      }
      setStatus('connecting')
      connection
        .start()
        .then(() => {
          if (cancelled) {
            // Cleanup ran during negotiation; stop the now-open connection.
            void connection.stop()
            return
          }
          setStatus('connected')
        })
        .catch(() => {
          if (!cancelled) {
            setStatus('disconnected')
            retryTimer = setTimeout(tryStart, RETRY_INTERVAL_MS)
          }
        })
    }

    connection.onreconnecting(() => {
      if (!cancelled) setStatus('reconnecting')
    })

    connection.onreconnected(() => {
      if (!cancelled) setStatus('connected')
    })

    connection.onclose(() => {
      if (!cancelled) {
        setStatus('disconnected')
        retryTimer = setTimeout(tryStart, RETRY_INTERVAL_MS)
      }
    })

    tryStart()

    return () => {
      cancelled = true
      if (retryTimer) clearTimeout(retryTimer)
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop()
      }
    }
  }, [setStatus])

  return connectionRef
}
