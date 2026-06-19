import { useEffect, type RefObject } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { notifications } from '@mantine/notifications'
import type { SessionFinishedPayload } from '../api/sessions'

/**
 * App-level handler for the broadcast `SessionFinished` event: an agent reached a turn-end with an
 * empty message queue, i.e. it is completely finished with what it was asked. Shows an in-app toast,
 * and — when the tab is backgrounded — a browser notification so you know even when not watching.
 *
 * Mount once alongside the other global SignalR hooks.
 */
export function useSessionFinishedToasts(connectionRef: RefObject<HubConnection | null>) {
  useEffect(() => {
    const connection = connectionRef.current
    if (!connection) return

    const handler = (payload: SessionFinishedPayload) => {
      const label = payload.label || 'Agent'
      const message = `${label} finished and is waiting.`

      notifications.show({
        title: 'Agent finished',
        message,
        color: 'green',
        autoClose: 6000,
      })

      // When the tab isn't visible, also raise a desktop notification.
      if (typeof Notification === 'undefined' || !document.hidden) return
      if (Notification.permission === 'granted') {
        new Notification('Agent finished', { body: message })
      } else if (Notification.permission === 'default') {
        void Notification.requestPermission()
      }
    }

    connection.on('SessionFinished', handler)
    return () => {
      connection.off('SessionFinished', handler)
    }
  }, [connectionRef])
}
