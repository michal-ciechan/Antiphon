import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { HomeTaskGroup, HomeTaskItemDto } from '../../../api/homeTasks'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../../test/utils'
import { server } from '../../../test/mocks/server'
import { normalizeDir } from '../projectGrouping'
import { GROUP_LABEL, GROUP_ORDER } from './homeTasksModel'
import { TasksSection } from './TasksSection'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

vi.mock('./HomeTaskModal', () => ({
  HomeTaskModal: ({ item }: { item: HomeTaskItemDto | null }) =>
    item ? <div data-testid="home-task-modal">{`${item.source}:${item.id}`}</div> : null,
}))

const DIR = 'C:\\src\\antiphon'
const DIR_KEY = normalizeDir(DIR)

function item(overrides: Partial<HomeTaskItemDto> = {}): HomeTaskItemDto {
  return {
    key: 'card:1',
    source: 'Card',
    id: '11111111-0000-0000-0000-000000000001',
    identifier: 'CARD-0001',
    title: 'A card',
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
    workingDirectory: DIR,
    repoPath: null,
    worktreePath: null,
    createdAt: '2026-09-01T10:00:00Z',
    startedAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    ...overrides,
  }
}

function seed({
  items = [] as HomeTaskItemDto[],
  status = 200,
}: {
  items?: HomeTaskItemDto[]
  status?: number
} = {}) {
  server.use(
    http.get('/api/home/tasks', () =>
      status === 200
        ? HttpResponse.json({ generatedAt: '2026-09-02T00:00:00Z', items })
        : new HttpResponse(null, { status }),
    ),
    http.get('/api/agents', () => HttpResponse.json([])),
    http.get('/api/attention', () =>
      HttpResponse.json({
        generatedAt: '2026-09-02T00:00:00Z',
        runnerConsulted: true,
        items: [],
      }),
    ),
  )
}

function renderSection(dirKeys = [DIR_KEY]) {
  return renderWithProviders(<TasksSection dirKeys={dirKeys} />)
}

describe('TasksSection', () => {
  it('renders the five groups in order with counts', async () => {
    seed({
      items: [
        item({
          key: 'need',
          id: 'need',
          identifier: 'CARD-0001',
          title: 'Needs a decision',
          group: 'NeedsHuman',
          state: 'NeedsDecision',
          humanReason: 'Decision',
        }),
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
        }),
        item({
          key: 'review',
          id: 'review',
          identifier: 'CARD-0003',
          title: 'Review card',
          group: 'Review',
          state: 'Review',
          humanReason: 'Review',
        }),
        item({
          key: 'next',
          id: 'next',
          identifier: 'CARD-0004',
          title: 'Backlog card',
          group: 'Next',
          state: 'Backlog',
          stage: null,
        }),
        item({
          key: 'done',
          id: 'done',
          identifier: 'CARD-0005',
          title: 'Finished card',
          group: 'Done',
          state: 'Done',
          stage: null,
          completedAt: '2026-09-01T18:00:00Z',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Needs a decision')

    const labels = GROUP_ORDER.map((group) => {
      const header = screen.getByTestId(`home-tasks-group-${group}`)
      expect(header).toHaveTextContent(GROUP_LABEL[group])
      expect(within(header).getByText('1')).toBeInTheDocument()
      return header
    })
    for (let i = 1; i < labels.length; i++) {
      expect(labels[i - 1].compareDocumentPosition(labels[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
  })

  it('always shows Needs-you and Running, with an empty line when they have no items', async () => {
    seed({
      items: [
        item({
          key: 'review',
          id: 'review',
          identifier: 'CARD-0003',
          title: 'Review card',
          group: 'Review',
          state: 'Review',
          humanReason: 'Review',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Review card')
    expect(screen.getByTestId('home-tasks-group-NeedsHuman')).toHaveTextContent('0')
    expect(screen.getByText('Nothing needs you.')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Running')).toHaveTextContent('0')
    expect(screen.getByText('Nothing running.')).toBeInTheDocument()
  })

  it('omits To review, Up next and Done when those groups are empty', async () => {
    seed({
      items: [
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Running card')
    expect(screen.queryByText('To review')).not.toBeInTheDocument()
    expect(screen.queryByText('Up next')).not.toBeInTheDocument()
    expect(screen.queryByText('Done')).not.toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-NeedsHuman')).toBeInTheDocument()
    expect(screen.getByTestId('home-tasks-group-Running')).toBeInTheDocument()
  })

  it('shows a +N more link when a capped group is cut', async () => {
    seed({
      items: [
        ...Array.from({ length: 9 }, (_, i) =>
          item({
            key: `review-${i}`,
            id: `review-${i}`,
            identifier: `CARD-${String(i + 1).padStart(4, '0')}`,
            title: `Review item ${i}`,
            group: 'Review' as HomeTaskGroup,
            state: 'Review',
            humanReason: 'Review',
          }),
        ),
        ...Array.from({ length: 13 }, (_, i) =>
          item({
            key: `done-${i}`,
            id: `done-${i}`,
            source: 'Delegation',
            identifier: `done${String(i).padStart(2, '0')}`,
            title: `Done item ${i}`,
            group: 'Done',
            state: 'Succeeded',
            boardId: null,
            stage: 'Docs',
            role: 'Docs',
            modelLevel: 'Medium',
            agentKind: 'ClaudeCode',
            completedAt: '2026-09-01T18:00:00Z',
          }),
        ),
      ],
    })
    renderSection()

    await screen.findByText('Review item 0')
    expect(screen.getByText('Review item 7')).toBeInTheDocument()
    expect(screen.queryByText('Review item 8')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: '+1 more → open board' })).toHaveAttribute(
      'href',
      '/boards/board-1',
    )

    expect(screen.getByText('Done item 0')).toBeInTheDocument()
    expect(screen.getByText('Done item 11')).toBeInTheDocument()
    expect(screen.queryByText('Done item 12')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: '+1 more → open delegations' })).toHaveAttribute(
      'href',
      '/orchestrator?tab=delegations',
    )
  })

  it('filters out items whose directories are not in dirKeys', async () => {
    seed({
      items: [
        item({
          key: 'mine',
          id: 'mine',
          identifier: 'CARD-0002',
          title: 'In this project',
          group: 'Running',
        }),
        item({
          key: 'other',
          id: 'other',
          identifier: 'CARD-0099',
          title: 'Other project card',
          group: 'Running',
          workingDirectory: 'C:\\src\\other',
          repoPath: null,
          worktreePath: null,
        }),
      ],
    })
    renderSection()

    await screen.findByText('In this project')
    expect(screen.queryByText('Other project card')).not.toBeInTheDocument()
  })

  it('shows a dimmed one-liner when the projection fails', async () => {
    seed({ status: 500 })
    renderSection()

    expect(
      await screen.findByText('Tasks are unavailable — the server did not answer for them.'),
    ).toBeInTheDocument()
    expect(screen.queryByTestId('home-tasks-group-NeedsHuman')).not.toBeInTheDocument()
  })

  it('opens the modal routed by source when a card is clicked', async () => {
    const cardId = '11111111-0000-0000-0000-000000000001'
    const taskId = 'aaaaaaaa-0000-0000-0000-000000000004'
    seed({
      items: [
        item({
          key: `card:${cardId}`,
          id: cardId,
          identifier: 'CARD-0002',
          title: 'Tasks section on the home rail',
          group: 'Running',
        }),
        item({
          key: `task:${taskId}`,
          source: 'Delegation',
          id: taskId,
          identifier: '15c3cb72',
          title: 'Rewrite the Windows install section',
          group: 'NeedsHuman',
          state: 'Blocked',
          humanReason: 'Question',
          boardId: null,
          stage: 'Docs',
          role: 'Docs',
          modelLevel: 'Medium',
          agentKind: 'ClaudeCode',
        }),
      ],
    })
    renderSection()

    await userEvent.click(await screen.findByRole('button', { name: 'Open CARD-0002' }))
    expect(screen.getByTestId('home-task-modal')).toHaveTextContent(`Card:${cardId}`)

    await userEvent.click(screen.getByRole('button', { name: 'Open 15c3cb72' }))
    expect(screen.getByTestId('home-task-modal')).toHaveTextContent(`Delegation:${taskId}`)
  })

  it('exposes #home-tasks-done as the ToReadBadge scroll target', async () => {
    seed({
      items: [
        item({
          key: 'run',
          id: 'run',
          identifier: 'CARD-0002',
          title: 'Running card',
          group: 'Running',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Running card')
    expect(document.getElementById('home-tasks-done')).not.toBeNull()
  })

  it('keeps #home-tasks-done on the Done group when it has items', async () => {
    seed({
      items: [
        item({
          key: 'done',
          id: 'done',
          identifier: 'CARD-0005',
          title: 'Finished card',
          group: 'Done',
          state: 'Done',
          completedAt: '2026-09-01T18:00:00Z',
        }),
      ],
    })
    renderSection()

    await screen.findByText('Finished card')
    await waitFor(() => {
      const target = document.getElementById('home-tasks-done')
      expect(target).not.toBeNull()
      expect(target).toHaveTextContent('Finished card')
    })
  })
})
