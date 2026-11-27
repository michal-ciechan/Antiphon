import { useEffect, useRef, type RefObject } from 'react'
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
  const seen = useRef(new Set<string>())
  useEffect(() => {
    const connection = connectionRef.current
    if (!connection) return

    const handler = (payload: AlertRaisedPayload) => {
      if (payload.severity !== 'Error' && payload.severity !== 'Critical') return
      if (seen.current.has(payload.id)) return
      seen.current.add(payload.id)
      // Reconnects and page reloads can replay a pending incident. Keep only IDs in tab storage.
      try {
        const key = 'antiphon.operator-alert.' + payload.id
        if (sessionStorage.getItem(key)) return
        sessionStorage.setItem(key, '1')
      } catch { /* Storage can be disabled. The mounted handler still deduplicates. */ }

      notifications.show({
        id: payload.id,
        title: `${payload.severity}: ${payload.title}`,
        message: payload.detail ?? payload.source,
        color: payload.severity === 'Critical' ? 'red' : 'orange',
        autoClose: payload.severity === 'Critical' ? false : 8000,
      })

      if (payload.severity !== 'Critical') return
      if (typeof Notification === 'undefined' || !document.hidden) return
      if (Notification.permission === 'granted') {
        new Notification(`Critical: ${payload.title}`, { body: payload.detail ?? payload.source, tag: payload.id })
      } else if (Notification.permission === 'default') {
        void Notification.requestPermission()
      }
    }

    const onAgentChanged = (event: { operatorAlert?: AlertRaisedPayload }) => {
      if (event.operatorAlert) handler(event.operatorAlert)
    }
    connection.on('AlertRaised', handler)
    connection.on('AgentChanged', onAgentChanged)
    return () => {
      connection.off('AlertRaised', handler)
      connection.off('AgentChanged', onAgentChanged)
    }
  }, [connectionRef])
}
