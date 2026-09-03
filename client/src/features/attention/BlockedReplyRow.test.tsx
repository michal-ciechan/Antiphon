import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { BlockedReplyRow } from './BlockedReplyRow'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const TASK = '11111111-1111-1111-1111-111111111111'

describe('BlockedReplyRow', () => {
  it('posts the answer to the task and clears itself', async () => {
    // The point of the whole slice: a blocked delegate is answered from the list, at the same
    // endpoint the drawer uses. If the box kept the text after sending, the next glance at the row
    // would read as "not sent yet" and invite a second answer into the same session.
    const bodies: unknown[] = []
    server.use(
      http.post(`/api/agent-tasks/${TASK}/reply`, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json({ id: TASK, status: 'Working' })
      }),
    )
    const done = vi.fn()

    renderWithProviders(<BlockedReplyRow taskId={TASK} onDone={done} />)

    const box = screen.getByRole('textbox', { name: 'Answer the delegate' })
    await userEvent.type(box, 'yes, accept negatives')
    await userEvent.click(screen.getByRole('button', { name: 'Send answer' }))

    await waitFor(() => expect(bodies).toEqual([{ message: 'yes, accept negatives', origin: 'Web' }]))
    await waitFor(() => expect(box).toHaveValue(''))
    expect(done).toHaveBeenCalled()
  })

  it('will not send an empty answer', async () => {
    // A blocked delegate is waiting on CONTENT. An empty reply would unblock it with nothing to act
    // on, which is strictly worse than leaving it blocked and visible.
    renderWithProviders(<BlockedReplyRow taskId={TASK} />)

    expect(screen.getByRole('button', { name: 'Send answer' })).toBeDisabled()
    await userEvent.type(screen.getByRole('textbox', { name: 'Answer the delegate' }), '   ')
    expect(screen.getByRole('button', { name: 'Send answer' })).toBeDisabled()
  })

  it('keeps the text when the send fails', async () => {
    // The answer is the only copy of what the human just typed; a failed POST must not eat it.
    server.use(
      http.post(`/api/agent-tasks/${TASK}/reply`, () =>
        HttpResponse.json({ title: 'the delegate is gone' }, { status: 409 }),
      ),
    )
    const done = vi.fn()

    renderWithProviders(<BlockedReplyRow taskId={TASK} onDone={done} />)
    const box = screen.getByRole('textbox', { name: 'Answer the delegate' })
    await userEvent.type(box, 'use the second branch')
    await userEvent.click(screen.getByRole('button', { name: 'Send answer' }))

    await waitFor(() => expect(box).toHaveValue('use the second branch'))
    expect(done).not.toHaveBeenCalled()
  })

  it('falls back to the bare form when the detail fetch fails', async () => {
    const bodies: unknown[] = []
    server.use(
      http.get(`/api/agent-tasks/${TASK}`, () => HttpResponse.json({ title: 'gone' }, { status: 500 })),
      http.post(`/api/agent-tasks/${TASK}/reply`, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json({ id: TASK, status: 'Working' })
      }),
    )

    renderWithProviders(<BlockedReplyRow taskId={TASK} evidence="Which branch?" />)

    expect(await screen.findByText('Which branch?')).toBeInTheDocument()
    const box = screen.getByRole('textbox', { name: 'Answer the delegate' })
    await userEvent.type(box, 'the left one')
    await userEvent.click(screen.getByRole('button', { name: 'Send answer' }))

    await waitFor(() => expect(bodies).toEqual([{ message: 'the left one', origin: 'Web' }]))
  })
})
