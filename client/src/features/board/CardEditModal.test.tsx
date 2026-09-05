import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { Notifications } from '@mantine/notifications'
import type { CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CardEditModal } from './CardEditModal'

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
    identifier: 'CARD-0019',
    title: 'Cards cannot be corrected',
    description: 'a record you cannot correct is a record that rots',
    importance: 'High', urgency: 'Normal', dueAt: null, urgentSince: null, effectiveUrgency: 'Normal', quadrant: 'Schedule', rank: 7,
    labels: ['board', 'record'],
    status: 'Backlog',
    concurrencyToken: 'token-1',
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    startedAt: null,
    completedAt: null,
    terminalReason: null,
    sessions: [],
    revisionCount: 4,
    archivedAt: null,
    archivedReason: null,
    archivedBy: null,
    ...overrides,
  }
}

function renderEdit(overrides: Partial<CardDto> = {}) {
  const onClose = vi.fn()
  renderWithProviders(
    <>
      <Notifications />
      <CardEditModal boardId="board-1" card={card(overrides)} onClose={onClose} />
    </>,
  )
  return { onClose }
}

const titleInput = () => screen.getByLabelText('Title') as HTMLInputElement
const descriptionInput = () => screen.getByLabelText('Description') as HTMLTextAreaElement
const reasonInput = () => screen.getByLabelText(/^Reason/) as HTMLTextAreaElement
const saveButton = () => screen.getByRole('button', { name: 'Save' })

describe('CardEditModal', () => {
  it('prefills the card as it stands', () => {
    renderEdit()
    expect(titleInput()).toHaveValue('Cards cannot be corrected')
    expect(screen.getByLabelText('Short alias')).toHaveValue('')
    expect(descriptionInput()).toHaveValue('a record you cannot correct is a record that rots')
    expect(screen.getByRole('textbox', { name: 'Importance' })).toHaveValue('High')
    expect(screen.getByLabelText('Labels')).toHaveValue('board, record')
  })

  it('prefills a stored short alias', () => {
    renderEdit({ alias: 'Check header' })
    expect(screen.getByLabelText('Short alias')).toHaveValue('Check header')
  })

  it('will not submit without a reason — a correction that does not say why is how a record rots', async () => {
    renderEdit()
    expect(saveButton()).toBeDisabled()

    await userEvent.type(reasonInput(), '   ')
    expect(saveButton()).toBeDisabled()

    await userEvent.type(reasonInput(), 'the title named the wrong bug')
    expect(saveButton()).toBeEnabled()
  })

  it('sends only the fields that changed, plus the token and editedBy', async () => {
    const patchSpy = vi.fn()
    server.use(http.patch('/api/cards/card-1/content', async ({ request }) => {
      patchSpy(await request.json())
      return HttpResponse.json(card({ title: 'Cards cannot be corrected, only closed' }))
    }))
    const { onClose } = renderEdit()

    await userEvent.clear(titleInput())
    await userEvent.type(titleInput(), 'Cards cannot be corrected, only closed')
    await userEvent.type(reasonInput(), 'the title named the wrong bug')
    await userEvent.click(saveButton())

    await waitFor(() => expect(patchSpy).toHaveBeenCalledWith({
      concurrencyToken: 'token-1',
      reason: 'the title named the wrong bug',
      title: 'Cards cannot be corrected, only closed',
      // Untouched fields go as null — the server reads null as "unchanged", so an untouched 20k
      // description is never rewritten by a one-word title fix.
      description: null,
      alias: null,
      importance: null,
      urgency: null,
      dueAt: null,
      clearDueAt: false,
      labels: null,
      editedBy: 'operator',
    }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('sends a changed short alias and an emptied one as a clear', async () => {
    const patchSpy = vi.fn()
    server.use(http.patch('/api/cards/card-1/content', async ({ request }) => {
      patchSpy(await request.json())
      return HttpResponse.json(card({ alias: 'Check header' }))
    }))
    const { onClose } = renderEdit()

    await userEvent.type(screen.getByLabelText('Short alias'), 'Check header')
    await userEvent.type(reasonInput(), 'give it a short label')
    await userEvent.click(saveButton())

    await waitFor(() => expect(patchSpy).toHaveBeenCalledWith(
      expect.objectContaining({ alias: 'Check header', title: null }),
    ))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('sends an emptied short alias as empty string so the server clears it', async () => {
    const patchSpy = vi.fn()
    server.use(http.patch('/api/cards/card-1/content', async ({ request }) => {
      patchSpy(await request.json())
      return HttpResponse.json(card({ alias: null }))
    }))
    renderEdit({ alias: 'Check header' })

    await userEvent.clear(screen.getByLabelText('Short alias'))
    await userEvent.type(reasonInput(), 'no longer needed')
    await userEvent.click(saveButton())

    await waitFor(() => expect(patchSpy).toHaveBeenCalledWith(
      expect.objectContaining({ alias: '', title: null }),
    ))
  })

  it('will not submit a sixth-word alias', async () => {
    renderEdit()
    await userEvent.type(screen.getByLabelText('Short alias'), 'one two three four five six')
    await userEvent.type(reasonInput(), 'too many words')
    expect(saveButton()).toBeDisabled()
    expect(screen.getByText('Alias must be at most 5 words.')).toBeInTheDocument()
  })

  it('sends a cleared description as empty string, which is a change, not "unchanged"', async () => {
    const patchSpy = vi.fn()
    server.use(http.patch('/api/cards/card-1/content', async ({ request }) => {
      patchSpy(await request.json())
      return HttpResponse.json(card({ description: '' }))
    }))
    renderEdit()

    await userEvent.clear(descriptionInput())
    await userEvent.type(reasonInput(), 'the description described a different card')
    await userEvent.click(saveButton())

    await waitFor(() => expect(patchSpy).toHaveBeenCalledWith(
      expect.objectContaining({ description: '', title: null }),
    ))
  })

  it('blocks an over-limit description client-side and turns its counter red', async () => {
    renderEdit()
    // Typing 20,001 characters through userEvent would take minutes; paste the value in.
    fireChange(descriptionInput(), 'x'.repeat(20_001))
    await userEvent.type(reasonInput(), 'pasted the whole spec by mistake')

    const counter = screen.getByText('20,001 / 20,000')
    expect(counter).toBeInTheDocument()
    expect(counter).toHaveStyle({ color: 'var(--mantine-color-red-text)' })
    expect(saveButton()).toBeDisabled()
  })

  it('lands a 422 on the input that caused it, message verbatim', async () => {
    server.use(http.patch('/api/cards/card-1/content', () =>
      HttpResponse.json({
        title: 'One or more validation errors occurred.',
        status: 422,
        errors: {
          Description: ['Description must be at most 20,000 characters; got 20,001.'],
        },
      }, { status: 422 })))
    renderEdit()

    await userEvent.type(descriptionInput(), '!')
    await userEvent.type(reasonInput(), 'a limit the client thought was higher')
    await userEvent.click(saveButton())

    // Verbatim, because CARD_LIMITS is a constant that can drift from CardService.Max*Length.
    expect(await screen.findByText('Description must be at most 20,000 characters; got 20,001.'))
      .toBeInTheDocument()
  })

  it('surfaces a 409 as a notification — it belongs to no single input', async () => {
    server.use(http.patch('/api/cards/card-1/content', () =>
      HttpResponse.json({
        title: 'Conflict',
        detail: "Card 'CARD-0019' was modified by another operation.",
        status: 409,
      }, { status: 409 })))
    const { onClose } = renderEdit()

    await userEvent.type(reasonInput(), 'racing another editor')
    await userEvent.click(saveButton())

    expect(await screen.findByText("Card 'CARD-0019' was modified by another operation."))
      .toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
  })
})

/** Sets a controlled input's value in one shot — 20k characters is not a thing to type. */
function fireChange(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(
    element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype,
    'value',
  )?.set
  setter?.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
}
