import { useEffect, useRef } from 'react'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { useConnectionStore } from '../stores/connectionStore'

const SIGNALR_HUB_URL = '/hubs/antiphon'

/**
 * Manages the SignalR connection lifecycle (connect, reconnect, disconnect).
 * Uses withAutomaticReconnect() for resilience and updates connectionStore status.
 */
export function useSignalR() {
  const connectionRef = useRef<HubConnection | null>(null)
  const setStatus = useConnectionStore((s) => s.setStatus)

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build()

    connectionRef.current = connection

    connection.onreconnecting(() => {
      setStatus('reconnecting')
    })

    connection.onreconnected(() => {
      setStatus('connected')
    })

    connection.onclose(() => {
      setStatus('disconnected')
    })

    setStatus('connecting')
    connection
      .start()
      .then(() => {
        setStatus('connected')
      })
      .catch((err) => {
        console.error('SignalR connection failed:', err)
        setStatus('disconnected')
      })

    return () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop()
      }
    }
  }, [setStatus])

  return connectionRef
}
