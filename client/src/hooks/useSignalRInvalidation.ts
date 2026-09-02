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
  boardId?: string
  cardId?: string
  agentId?: string
  taskId?: string
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
  {
    event: 'BoardChanged',
    getKeys: (p) => [['boards'], ...(p.boardId ? [['boards', p.boardId]] : []), ['homeTasks']],
  },
  {
    event: 'WorkflowReloaded',
    getKeys: (p) => [
      ...(p.boardId ? [['boards', p.boardId], ['boards', p.boardId, 'workflow']] : []),
    ],
  },
  {
    event: 'CardChanged',
    // The thread projection is card-scoped but keyed by whichever identifier form the caller
    // used, so the prefix is the only safe invalidation target.
    getKeys: (p) => [
      ['boards'],
      ...(p.boardId ? [['boards', p.boardId]] : []),
      ['cards', 'list'],
      ['cards', 'thread'],
      // A card parked for (or moved out of) a human decision changes the same attention feed as
      // delegated-task events. This makes the decision chip and panel update on the next paint.
      ['attention'],
      ['homeTasks'],
    ],
  },
  {
    event: 'RunAttemptChanged',
    getKeys: (p) => [
      ['orchestrator', 'state'],
      ['boards'],
      ...(p.boardId ? [['boards', p.boardId]] : []),
      ['cards', 'list'],
      ['homeTasks'],
    ],
  },
  {
    event: 'AgentChanged',
    getKeys: (p) => [
      ['agents', 'list'],
      ...(p.agentId ? [['agents', 'detail', p.agentId]] : []),
      // An agent's incidents and its session's fate both feed the attention projection.
      ['attention'],
      ['homeTasks'],
    ],
  },
  {
    event: 'AgentQueueChanged',
    getKeys: (p) => [
      ['agents', 'list'],
      ...(p.agentId ? [['agents', 'detail', p.agentId], ['agents', 'queue', p.agentId]] : []),
      ['boards'],
      ...(p.boardId ? [['boards', p.boardId]] : []),
      ['homeTasks'],
    ],
  },
  {
    event: 'SessionStarted',
    getKeys: (p) => [['boards'], ...(p.boardId ? [['boards', p.boardId]] : [])],
  },
  {
    event: 'SessionExited',
    getKeys: (p) => [['boards'], ...(p.boardId ? [['boards', p.boardId]] : [])],
  },
  {
    event: 'SessionFinished',
    getKeys: (p) => [
      ['agents', 'list'],
      ...(p.agentId ? [['agents', 'detail', p.agentId]] : []),
      ['boards'],
      ...(p.boardId ? [['boards', p.boardId]] : []),
      ['homeTasks'],
    ],
  },
  {
    event: 'OrchestratorTick',
    getKeys: () => [['orchestrator', 'state']],
  },
  {
    // Delegated tasks change from three directions — the dispatcher, the delegate's turn-end, and
    // the board's own actions — so the board is only ever live if it listens.
    event: 'AgentTaskChanged',
    getKeys: (p) => [
      ['agentTasks', 'list'],
      ...(p.taskId ? [['agentTasks', 'detail', p.taskId]] : []),
      // CARD-0035 §D5 rules out a new SignalR event for the attention projection: every condition
      // it computes is derived from state that already broadcasts, so it rides these.
      ['attention'],
      // The thread's task rows are correlated by citation, not by key, so no payload field can
      // narrow this — any task change may belong to any open thread.
      ['cards', 'thread'],
      ['homeTasks'],
    ],
  },
  {
    event: 'ChannelChanged',
    getKeys: () => [['channels']],
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
