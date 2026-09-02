import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { CardDto } from '../../../api/boards'
import type { HomeTaskItemDto } from '../../../api/homeTasks'
import { renderWithProviders, screen, waitFor } from '../../../test/utils'
import { server } from '../../../test/mocks/server'
import { HomeTaskModal } from './HomeTaskModal'

vi.mock('../../board/CardModal', () => ({
  CardModal: ({ card }: { card: CardDto | null }) => (
    <div data-testid="card-modal">{card ? card.title : 'CREATE'}</div>
  ),
}))

vi.mock('../../delegations/DelegationTaskModal', () => ({
  DelegationTaskModal: ({ taskId }: { taskId: string }) => (
    <div data-testid="delegation-modal">{taskId}</div>
  ),
}))

const CARD_ID = '11111111-0000-0000-0000-000000000001'
const TASK_ID = 'aaaaaaaa-0000-0000-0000-000000000004'

function item(overrides: Partial<HomeTaskItemDto> = {}): HomeTaskItemDto {
  return {
    key: 'card:1',
    source: 'Card',
    id: CARD_ID,
    identifier: 'CARD-0002',
    title: 'Tasks section on the home rail',
    terminalReason: null,
    group: 'Running',
    state: 'InProgress',
    humanReason: null,
    stage: 'Plan',
    workflowRunStatus: null,
    priority: 1,
    boardId: 'board-1',
    worker: null,
    ownerAgentId: null,
    agentKind: null,
    modelLevel: null,
    escalatedFrom: null,
    role: null,
    costUsd: null,
    agentId: null,
    agentName: null,
    agentSessionId: null,
    readAt: null,
    deliverablePath: null,
    deliverableRef: null,
    workingDirectory: 'C:\\src\\antiphon',
    repoPath: null,
    worktreePath: null,
    createdAt: '2026-09-01T10:00:00Z',
    startedAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    ...overrides,
  }
}

function cardDto(): CardDto {
  return {
    id: CARD_ID,
    boardId: 'board-1',
    boardColumnId: 'column-inprogress',
    ownerSessionId: null,
    currentWorktreeId: null,
    assignedAgentId: null,
    assignedAgentName: null,
    agentQueuePosition: null,
    activeWorkflowRunId: null,
    workflowRunStatus: null,
    currentWorkflowStageName: null,
    identifier: 'CARD-0002',
    title: 'Tasks section on the home rail',
    description: 'Rail list of cards and delegations',
    priority: 1,
    labels: [],
    status: 'InProgress',
    concurrencyToken: 'token-1',
    createdAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    startedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    terminalReason: null,
    sessions: [],
    revisionCount: 0,
    archivedAt: null,
    archivedReason: null,
    archivedBy: null,
  }
}

describe('HomeTaskModal', () => {
  it('waits for useCard before mounting CardModal so the create form never flashes', async () => {
    let resolveCard!: (value: CardDto) => void
    const cardPromise = new Promise<CardDto>((resolve) => {
      resolveCard = resolve
    })
    server.use(
      http.get('/api/cards/:id', async () => HttpResponse.json(await cardPromise)),
      http.get('/api/boards/:id/columns', () => HttpResponse.json([])),
    )

    renderWithProviders(<HomeTaskModal item={item()} onClose={() => {}} />)

    expect(await screen.findByLabelText('Loading card')).toBeInTheDocument()
    expect(screen.queryByTestId('card-modal')).not.toBeInTheDocument()
    expect(screen.queryByText('CREATE')).not.toBeInTheDocument()

    resolveCard(cardDto())

    expect(await screen.findByTestId('card-modal')).toHaveTextContent('Tasks section on the home rail')
    expect(screen.queryByText('CREATE')).not.toBeInTheDocument()
    await waitFor(() => expect(screen.queryByLabelText('Loading card')).not.toBeInTheDocument())
  })

  it('mounts DelegationTaskModal for a delegation item', () => {
    renderWithProviders(
      <HomeTaskModal
        item={item({
          key: `task:${TASK_ID}`,
          source: 'Delegation',
          id: TASK_ID,
          identifier: 'aaaaaaaa',
          group: 'NeedsHuman',
          state: 'Blocked',
          boardId: null,
        })}
        onClose={() => {}}
      />,
    )

    expect(screen.getByTestId('delegation-modal')).toHaveTextContent(TASK_ID)
    expect(screen.queryByTestId('card-modal')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Loading card')).not.toBeInTheDocument()
  })
})
