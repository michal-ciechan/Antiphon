import { useEffect, useRef } from 'react'
import { notifications } from '@mantine/notifications'
import { useConnectionStore, type ConnectionStatus } from '../../stores/connectionStore'

/**
 * Hook that manages toast notifications for:
 * 1. SignalR connection status changes (disconnection warning, reconnection success)
 * 2. Background workflow events (stage completions, gate readiness)
 *
 * Rules (UX-DR24):
 * - Toasts auto-dismiss after 3-5 seconds
 * - Max 3 visible at a time (managed by @mantine/notifications)
 * - No toasts for user-initiated actions
 * - Disconnection toast is persistent until reconnected
 */
export function useToastNotifications() {
  const status = useConnectionStore((s) => s.status)
  const prevStatusRef = useRef<ConnectionStatus>(status)
  useEffect(() => {
    const prevStatus = prevStatusRef.current
    prevStatusRef.current = status

    // Connection status is shown via the header indicator — no toasts for connect/disconnect.
    // Only show a brief toast when fully reconnected after a disruption.
    if (status === 'connected' && prevStatus === 'reconnecting') {
      notifications.show({
        title: 'Reconnected',
        message: 'Real-time updates restored.',
        color: 'green',
        autoClose: 3000,
      })
    }
  }, [status])
}

/**
 * Show a toast notification for a background workflow event.
 * Call this from SignalR event handlers for events not triggered by the current user.
 */
export function showWorkflowToast(opts: {
  title: string
  message: string
  workflowId?: string
  color?: string
}) {
  notifications.show({
    title: opts.title,
    message: opts.message,
    color: opts.color ?? 'blue',
    autoClose: 4000,
    onClick: () => {
      if (opts.workflowId) {
        // Navigate to workflow - uses window.location since we can't use hooks outside React
        window.location.href = `/workflow/${opts.workflowId}`
      }
    },
    style: opts.workflowId ? { cursor: 'pointer' } : undefined,
  })
}
