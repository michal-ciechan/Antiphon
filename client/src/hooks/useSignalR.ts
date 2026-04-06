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

/**
 * Manages the SignalR connection lifecycle (connect, reconnect, disconnect).
 * Uses withAutomaticReconnect() for resilience and updates connectionStore status.
 * After all automatic reconnect attempts are exhausted, keeps retrying every 5s
 * so the connection recovers after a server restart without requiring a page refresh.
 */
export function useSignalR() {
  const connectionRef = useRef<HubConnection | null>(null)
  const setStatus = useConnectionStore((s) => s.setStatus)
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const stoppedRef = useRef(false)

  useEffect(() => {
    stoppedRef.current = false

    const connection = new HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connectionRef.current = connection

    const tryStart = () => {
      if (stoppedRef.current) return
      setStatus('connecting')
      connection
        .start()
        .then(() => {
          setStatus('connected')
        })
        .catch(() => {
          if (!stoppedRef.current) {
            setStatus('disconnected')
            retryTimerRef.current = setTimeout(tryStart, RETRY_INTERVAL_MS)
          }
        })
    }

    connection.onreconnecting(() => {
      setStatus('reconnecting')
    })

    connection.onreconnected(() => {
      setStatus('connected')
    })

    connection.onclose(() => {
      if (!stoppedRef.current) {
        setStatus('disconnected')
        retryTimerRef.current = setTimeout(tryStart, RETRY_INTERVAL_MS)
      }
    })

    tryStart()

    return () => {
      stoppedRef.current = true
      if (retryTimerRef.current) clearTimeout(retryTimerRef.current)
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop()
      }
    }
  }, [setStatus])

  return connectionRef
}
