import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CardDiscussionPanel } from './CardDiscussionPanel'

describe('CardDiscussionPanel', () => {
  it('lists discussion comments and posts a new Antiphon comment', async () => {
    const posted: unknown[] = []
    server.use(
      http.get('/api/cards/card-1/discussion', () =>
        HttpResponse.json([
          {
            id: 'c-ext',
            cardId: 'card-1',
            body: 'From GitHub',
            author: 'alice',
            origin: 'External',
            externalCommentId: '11',
            externalUrl: 'https://github.test/acme/app/issues/1#issuecomment-11',
            createdAt: '2026-08-24T10:00:00Z',
            syncedAt: null,
          },
        ]),
      ),
      http.post('/api/cards/card-1/discussion', async ({ request }) => {
        const body = await request.json()
        posted.push(body)
        return HttpResponse.json(
          {
            id: 'c-new',
            cardId: 'card-1',
            body: (body as { body: string }).body,
            author: (body as { author?: string }).author ?? 'operator',
            origin: 'Antiphon',
            externalCommentId: null,
            externalUrl: null,
            createdAt: '2026-08-24T12:00:00Z',
            syncedAt: null,
          },
          { status: 201 },
        )
      }),
    )

    renderWithProviders(<CardDiscussionPanel cardId="card-1" />)

    expect(await screen.findByText('From GitHub')).toBeInTheDocument()
    expect(screen.getByText('GitHub')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Discussion comment'), 'Local reply')
    await userEvent.click(screen.getByRole('button', { name: 'Post' }))

    await waitFor(() =>
      expect(posted).toEqual([{ body: 'Local reply', author: 'operator' }]),
    )
  })

  it('shows an empty state when there are no comments', async () => {
    server.use(
      http.get('/api/cards/card-2/discussion', () => HttpResponse.json([])),
    )

    renderWithProviders(<CardDiscussionPanel cardId="card-2" />)

    expect(await screen.findByTestId('card-discussion-empty')).toBeInTheDocument()
  })
})
