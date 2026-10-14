import { describe, expect, it, vi } from 'vitest'
import type { CardDto } from '../../api/boards'
import { renderWithProviders, screen, within } from '../../test/utils'
import { BoardCard } from './BoardCard'

function card(overrides: Partial<CardDto> = {}): CardDto {
  return {
    id: 'card-1',
    boardId: 'board-1',
    boardColumnId: 'column-backlog',
    ownerSessionId: null,
    currentWorktreeId: null,
    assignedAgentId: null,
    assignedAgentName: null,
    agentQueuePosition: null,
    activeWorkflowRunId: null,
    workflowRunStatus: null,
    currentWorkflowStageName: null,
    identifier: 'CARD-0325',
    title: 'From GitHub',
    description: '',
    importance: 'Normal',
    urgency: 'Normal',
    dueAt: null,
    urgentSince: null,
    effectiveUrgency: 'Normal',
    quadrant: 'Someday',
    rank: 10,
    labels: [],
    status: 'Backlog',
    concurrencyToken: 'token-1',
    createdAt: '2026-09-02T12:00:00Z',
    updatedAt: '2026-09-02T12:00:00Z',
    startedAt: null,
    completedAt: null,
    terminalReason: null,
    sessions: [],
    revisionCount: 0,
    archivedAt: null,
    archivedReason: null,
    archivedBy: null,
    ...overrides,
  }
}

describe('BoardCard', () => {
  it('shows a Blocked chip naming the blockers when openBlockerCount is positive', () => {
    const { rerender } = renderWithProviders(<BoardCard card={card({
      openBlockerCount: 1,
      blockers: [
        { identifier: 'CARD-0049', status: 'InProgress', resolved: false },
        { identifier: 'CARD-0048', status: 'Done', resolved: true },
      ],
    })} onOpen={vi.fn()} />)
    const tile = screen.getByRole('article', { name: 'CARD-0325 From GitHub' })
    expect(within(tile).getByText('Blocked')).toHaveAttribute('title', 'Blocked by CARD-0049')
    rerender(<BoardCard card={card({
      openBlockerCount: 0,
      blockers: [{ identifier: 'CARD-0049', status: 'Done', resolved: true }],
    })} onOpen={vi.fn()} />)
    expect(within(tile).queryByText('Blocked')).toBeNull()
  })

  it('shows no Blocked chip when openBlockerCount is zero or absent', () => {
    const { rerender } = renderWithProviders(<BoardCard card={card()} onOpen={vi.fn()} />)
    const tile = screen.getByRole('article', { name: 'CARD-0325 From GitHub' })
    expect(within(tile).queryByText('Blocked')).toBeNull()
    rerender(<BoardCard card={card({ openBlockerCount: 0 })} onOpen={vi.fn()} />)
    expect(within(tile).queryByText('Blocked')).toBeNull()
  })

  it('shows a review chip next to the GitHub key when the import needs a human rating', () => {
    renderWithProviders(
      <BoardCard
        card={card({
          externalIssue: {
            trackerKind: 'GitHubIssues',
            key: '#30',
            url: 'https://github.test/acme/app/issues/30',
            author: 'bob',
            authorIsOperator: false,
            needsHumanReview: true,
          },
        })}
        onOpen={vi.fn()}
      />,
    )
    const tile = screen.getByRole('article', { name: 'CARD-0325 From GitHub' })
    expect(within(tile).getByText('GH #30')).toBeInTheDocument()
    expect(within(tile).getByText('review')).toBeInTheDocument()
  })

  it('does not show the review chip when the import does not need review', () => {
    renderWithProviders(
      <BoardCard
        card={card({
          externalIssue: {
            trackerKind: 'GitHubIssues',
            key: '#30',
            url: 'https://github.test/acme/app/issues/30',
            needsHumanReview: false,
          },
        })}
        onOpen={vi.fn()}
      />,
    )
    expect(screen.getByText('GH #30')).toBeInTheDocument()
    expect(screen.queryByText('review')).not.toBeInTheDocument()
  })
})
