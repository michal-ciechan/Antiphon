import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DiffReview } from './DiffReview'

const reviewCard: CardDto = {
  id: 'card-1',
  boardId: 'board-1',
  boardColumnId: 'column-review',
  ownerSessionId: 'session-1',
  currentWorktreeId: 'worktree-1',
  identifier: 'CARD-0001',
  title: 'Review diff',
  description: 'Inspect changes',
  priority: 1,
  labels: ['review'],
  status: 'Review',
  concurrencyToken: 'token-1',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  startedAt: '2026-01-01T00:00:00Z',
  completedAt: null,
  terminalReason: null,
  sessions: [],
}

function diffHandler() {
  return http.get('/api/cards/card-1/diff', () =>
    HttpResponse.json({
      baseBranch: 'main',
      headBranch: 'feat/card-CARD-0001',
      files: [
        {
          filename: 'src/App.tsx',
          additions: 2,
          deletions: 1,
          patch: [
            'diff --git a/src/App.tsx b/src/App.tsx',
            '@@ -1,2 +1,3 @@',
            '-old line',
            '+new line',
            '+second new line',
          ].join('\n'),
        },
      ],
    }),
  )
}

describe('DiffReview', () => {
  it('renders added and removed lines from the card diff', async () => {
    server.use(diffHandler())

    renderWithProviders(<DiffReview boardId="board-1" card={reviewCard} />)

    expect(await screen.findByText('+new line')).toBeInTheDocument()
    expect(screen.getByText('-old line')).toBeInTheDocument()
    expect(screen.getByText('src/App.tsx')).toBeInTheDocument()
    expect(screen.getAllByTestId('diff-line-added')[0]).toHaveTextContent('+new line')
    expect(screen.getByTestId('diff-line-removed')).toHaveTextContent('-old line')
  })

  it('posts an inline review comment to the card comments API', async () => {
    const postSpy = vi.fn()
    server.use(
      diffHandler(),
      http.post('/api/cards/card-1/comments', async ({ request }) => {
        postSpy(await request.json())
        return HttpResponse.json({
          cardId: 'card-1',
          sessionId: 'session-1',
          formattedMessage: 'Review comment',
        }, { status: 202 })
      }),
    )

    renderWithProviders(<DiffReview boardId="board-1" card={reviewCard} />)

    await userEvent.click(await screen.findByLabelText('Comment on src/App.tsx new line 1'))
    await userEvent.type(screen.getByLabelText('Comment for src/App.tsx new line 1'), 'Please check this edge case.')
    await userEvent.click(screen.getByLabelText('Send comment for src/App.tsx new line 1'))

    await waitFor(() => expect(postSpy).toHaveBeenCalledWith({
      message: 'Please check this edge case.',
      filePath: 'src/App.tsx',
      line: 1,
      endLine: 1,
      side: 'new',
    }))
  })

  it('posts a selected diff line range to the card comments API', async () => {
    const postSpy = vi.fn()
    const user = userEvent.setup()
    server.use(
      diffHandler(),
      http.post('/api/cards/card-1/comments', async ({ request }) => {
        postSpy(await request.json())
        return HttpResponse.json({
          cardId: 'card-1',
          sessionId: 'session-1',
          formattedMessage: 'Review comment',
        }, { status: 202 })
      }),
    )

    renderWithProviders(<DiffReview boardId="board-1" card={reviewCard} />)

    await user.click(await screen.findByLabelText('Comment on src/App.tsx new line 1'))
    await user.keyboard('{Shift>}')
    await user.click(screen.getByLabelText('Comment on src/App.tsx new line 2'))
    await user.keyboard('{/Shift}')
    await user.type(screen.getByLabelText('Comment for src/App.tsx new lines 1-2'), 'Please revise both lines.')
    await user.click(screen.getByLabelText('Send comment for src/App.tsx new lines 1-2'))

    await waitFor(() => expect(postSpy).toHaveBeenCalledWith({
      message: 'Please revise both lines.',
      filePath: 'src/App.tsx',
      line: 1,
      endLine: 2,
      side: 'new',
    }))
  })
})
