import { useEffect, type RefObject } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { notifications } from '@mantine/notifications'

export interface AlertRaisedPayload {
  id: string
  severity: 'Info' | 'Warning' | 'Error' | 'Critical'
  source: string
  title: string
  detail: string | null
  agentId: string | null
  createdAt: string
}

/**
 * App-level handler for the broadcast `AlertRaised` event (supervision/reconciler/launch alerts).
 * Error+ shows an in-app toast (Warning/Info stay in the incidents drawer and the alert sinks);
 * Critical also raises a desktop notification when the tab is hidden.
 *
 * Mount once alongside the other global SignalR hooks.
 */
export function useAlertToasts(connectionRef: RefObject<HubConnection | null>) {
  useEffect(() => {
    const connection = connectionRef.current
    if (!connection) return

    const handler = (payload: AlertRaisedPayload) => {
      if (payload.severity !== 'Error' && payload.severity !== 'Critical') return

      notifications.show({
        title: `${payload.severity}: ${payload.title}`,
        message: payload.detail ?? payload.source,
        color: payload.severity === 'Critical' ? 'red' : 'orange',
        autoClose: payload.severity === 'Critical' ? false : 8000,
      })

      if (payload.severity !== 'Critical') return
      if (typeof Notification === 'undefined' || !document.hidden) return
      if (Notification.permission === 'granted') {
        new Notification(`Critical: ${payload.title}`, { body: payload.detail ?? payload.source })
      } else if (Notification.permission === 'default') {
        void Notification.requestPermission()
      }
    }

    connection.on('AlertRaised', handler)
    return () => {
      connection.off('AlertRaised', handler)
    }
  }, [connectionRef])
}
