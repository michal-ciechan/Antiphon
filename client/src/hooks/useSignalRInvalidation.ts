import { useEffect, type RefObject } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { keepSixKeys } from '../api/keepSix'

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
  // `as const` query keys (keepSixKeys.list) are readonly tuples. A mutable
  // unknown[][] rejects them (TS4104 / TS2322).
  getKeys: (payload: EventPayload) => ReadonlyArray<readonly unknown[]>
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
    getKeys: (p) => [['boards'], ['card-file-status'], ['projects'], ...(p.boardId ? [['boards', p.boardId]] : []), ['homeTasks']],
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
      ['card-file-status'],
      // The full card is keyed ['cards', 'detail', id] (boardKeys.card), which ['cards', id] does
      // not prefix-match; without it an open card keeps a stale concurrency token (CARD-1256).
      ...(p.cardId ? [['private-notes', p.cardId], ['cards', p.cardId], ['cards', 'detail', p.cardId]] : []),
      ['cards', 'list'],
      ['cards', 'thread'],
      // A card parked for (or moved out of) a human decision changes the same attention feed as
      // delegated-task events. This makes the decision chip and panel update on the next paint.
      ['attention'],
      ['homeTasks'],
      ['agentTasks', 'pipeline'],
      keepSixKeys.list,
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
      ['agentTasks', 'pipeline'],
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
      ['agentTasks', 'pipeline'],
    ],
  },
  {
    event: 'OrchestratorTick',
    getKeys: () => [['orchestrator', 'state'], keepSixKeys.list],
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
      ['agentTasks', 'pipeline'],
      keepSixKeys.list,
    ],
  },
  {
    event: 'ChannelChanged',
    getKeys: () => [['channels']],
  },
  {
    event: 'RunnerDefaultsChanged',
    getKeys: () => [['runnerDefaults'], ['runnerDefaultRevisions']],
  },
  {
    // CARD-0506 D-9: every concurrency query key shares the 'dispatchConcurrency' prefix, and the
    // pipeline projection carries the same effective limits, so one event refreshes both. The payload
    // names a revision only; the data comes from the scope GET (useSignalRInvalidation.test.ts V07).
    event: 'DispatchConcurrencyChanged',
    getKeys: () => [['dispatchConcurrency'], ['agentTasks', 'pipeline']],
  },
  {
    event: 'ScheduleChanged',
    getKeys: (p) => [
      ['schedules'],
      ...(p.agentId ? [['schedules', 'agent', p.agentId]] : []),
      ...(p.cardId ? [['schedules', 'card', p.cardId]] : []),
      ['attention'],
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
