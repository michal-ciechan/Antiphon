import { useEffect, type RefObject } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'

/**
 * SignalR event → TanStack Query invalidation mapping.
 * Subscribes to SignalR events and invalidates the corresponding query keys
 * so that all features benefit from real-time updates automatically.
 */

interface EventPayload {
  workflowId?: string
  [key: string]: unknown
}

type InvalidationMapping = {
  event: string
  getKeys: (payload: EventPayload) => unknown[][]
}

const INVALIDATION_MAP: InvalidationMapping[] = [
  {
    event: 'WorkflowStatusChanged',
    getKeys: (p) => [['workflows'], ...(p.workflowId ? [['workflow', p.workflowId]] : [])],
  },
  {
    event: 'StageCompleted',
    getKeys: (p) => [
      ...(p.workflowId
        ? [['workflow', p.workflowId, 'stages'], ['workflow', p.workflowId]]
        : []),
    ],
  },
  {
    event: 'GateReady',
    getKeys: (p) => [['workflows'], ...(p.workflowId ? [['workflow', p.workflowId]] : [])],
  },
  {
    event: 'GateActioned',
    getKeys: (p) => [['workflows'], ...(p.workflowId ? [['workflow', p.workflowId]] : [])],
  },
  {
    event: 'ArtifactUpdated',
    getKeys: (p) => [...(p.workflowId ? [['workflow', p.workflowId, 'artifacts']] : [])],
  },
  {
    event: 'CascadeTriggered',
    getKeys: (p) => [
      ...(p.workflowId
        ? [['workflow', p.workflowId, 'stages'], ['workflow', p.workflowId]]
        : []),
    ],
  },
]

export function useSignalRInvalidation(connectionRef: RefObject<HubConnection | null>) {
  const queryClient = useQueryClient()

  useEffect(() => {
    const connection = connectionRef.current
    if (!connection) return

    const handlers: Array<{ event: string; handler: (payload: EventPayload) => void }> = []

    for (const mapping of INVALIDATION_MAP) {
      const handler = (payload: EventPayload) => {
        const keys = mapping.getKeys(payload)
        for (const key of keys) {
          queryClient.invalidateQueries({ queryKey: key })
        }
      }
      connection.on(mapping.event, handler)
      handlers.push({ event: mapping.event, handler })
    }

    return () => {
      for (const { event, handler } of handlers) {
        connection.off(event, handler)
      }
    }
  }, [connectionRef, queryClient])
}
