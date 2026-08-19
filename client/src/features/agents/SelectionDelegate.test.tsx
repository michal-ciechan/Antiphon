import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SelectionComposer } from './SelectionDelegate'
import { buildSelectionGoal } from './selectionGoal'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

interface CreateBody {
  goal: string
  kind: string
  role: string
  workspace: string | null
  workingDirectory: string | null
  scopeGlob: string | null
}

function captureCreate(): { body: CreateBody | null } {
  const captured: { body: CreateBody | null } = { body: null }
  server.use(
    http.post('/api/agent-tasks', async ({ request }) => {
      captured.body = (await request.json()) as CreateBody
      return HttpResponse.json({
        id: 'task-1',
        shortId: '1a2b3c4d',
        status: 'Queued',
        modelLevel: 'Medium',
        warning: null,
      })
    }),
  )
  return captured
}

describe('buildSelectionGoal', () => {
  it('quotes every selected line so the delegate sees exactly what was pointed at', () => {
    expect(buildSelectionGoal('docs/plan.md', 'first line\nsecond line', 'tighten this up')).toBe(
      'In docs/plan.md:\n\n> first line\n> second line\n\ntighten this up',
    )
  })
})

describe('SelectionComposer', () => {
  const props = {
    filePath: 'docs/plan.md',
    workingDirectory: 'C:\\src\\antiphon',
    selection: 'The deploy step is manual for now.',
    defaultRole: 'Docs' as const,
    onClose: vi.fn(),
  }

  it('queues a delegation for the pool: quoted goal, server-decided workspace, file-path scope', async () => {
    const captured = captureCreate()
    renderWithProviders(<SelectionComposer {...props} />)

    await userEvent.type(
      screen.getByPlaceholderText('What should be done about this passage?'),
      'automate it',
    )
    await userEvent.click(screen.getByTestId('selection-queue'))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({
      goal: 'In docs/plan.md:\n\n> The deploy step is manual for now.\n\nautomate it',
      kind: 'Worker',
      role: 'Docs',
      // null = the server decides — the Shared default is what lets the warm pool pick it up.
      workspace: null,
      workingDirectory: 'C:\\src\\antiphon',
      scopeGlob: 'docs/plan.md',
    })
  })

  it('lets the quick role chips change the tier decision', async () => {
    const captured = captureCreate()
    renderWithProviders(<SelectionComposer {...props} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Plan' }))
    await userEvent.type(
      screen.getByPlaceholderText('What should be done about this passage?'),
      'decide the approach',
    )
    await userEvent.click(screen.getByTestId('selection-queue'))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body?.role).toBe('Plan')
  })

  it('will not queue an empty instruction — the quote alone is not a goal', () => {
    renderWithProviders(<SelectionComposer {...props} />)
    expect(screen.getByTestId('selection-queue')).toBeDisabled()
  })
})
