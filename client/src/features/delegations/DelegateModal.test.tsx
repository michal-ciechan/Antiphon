import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DelegateModal } from './DelegateModal'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

interface CreateBody {
  goal: string
  kind: string
  role: string
  modelLevel: string | null
  workspace: string
  workingDirectory: string | null
  scopeGlob: string | null
  denyDirectEdits: boolean | null
}

function captureCreate(warning: string | null = null): { body: CreateBody | null } {
  const captured: { body: CreateBody | null } = { body: null }
  server.use(
    http.post('/api/agent-tasks', async ({ request }) => {
      captured.body = (await request.json()) as CreateBody
      return HttpResponse.json({
        id: 'task-1',
        shortId: '1a2b3c4d',
        status: 'Queued',
        modelLevel: 'Medium',
        warning,
      })
    }),
  )
  return captured
}

describe('DelegateModal', () => {
  it('defaults to a worker in the shared directory — isolation is opt-in', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.type(screen.getByLabelText('Goal'), 'rename the install section')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ kind: 'Worker', workspace: 'Shared', modelLevel: null })
  })

  it('lets the role carry the tier rather than asking for one', async () => {
    // The role IS the cost decision; sending an explicit level would bypass the policy that makes
    // "run the tests" cheap and "write the code" expensive.
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Test' }))
    expect(screen.getByTestId('tier-Low')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Goal'), 'run the suite and report failures')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ role: 'Test', modelLevel: null })
  })

  it('never shows a sub-orchestrator running below opus', async () => {
    // Decomposition is the expensive kind of thinking, and the server floors it — showing a
    // cheaper tier here would promise something it will not do.
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Docs' }))
    expect(screen.getByTestId('tier-Medium')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    expect(screen.queryByTestId('tier-Medium')).not.toBeInTheDocument()
    expect(screen.getByTestId('tier-Frontier')).toBeInTheDocument()
  })

  it('makes a sub-orchestrator a Plan in its own worktree, with the deny hook armed', async () => {
    // It decomposes (Plan) and fans out writers (worktree + deny hook). All three overridable.
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    expect(screen.getByRole('radio', { name: 'Plan' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Worktree' })).toBeChecked()

    await userEvent.type(screen.getByLabelText('Goal'), 'get the Postgres 18 upgrade shipped')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({
      kind: 'Orchestrator',
      role: 'Plan',
      workspace: 'Worktree',
      denyDirectEdits: true,
    })
  })

  it('lets the deny hook be turned off for an orchestrator that must write its plan', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    await userEvent.click(screen.getByRole('switch', { name: /Block direct edits/ }))

    await userEvent.type(screen.getByLabelText('Goal'), 'plan then run the migration')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body?.denyDirectEdits).toBe(false)
  })

  it('never sends the deny flag for a worker — its whole job is to edit', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect(screen.queryByRole('switch', { name: /Block direct edits/ })).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Goal'), 'fix the typo')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body?.denyDirectEdits).toBeNull()
  })

  it('surfaces the server’s warning — legal but risky needs saying at creation', async () => {
    const { notifications } = await import('@mantine/notifications')
    captureCreate('This orchestrator runs directly in its caller’s directory.')
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    await userEvent.click(screen.getByRole('radio', { name: 'Shared' }))
    await userEvent.type(screen.getByLabelText('Goal'), 'orchestrate in place')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(
        expect.objectContaining({ color: 'yellow', message: expect.stringContaining('caller’s directory') }),
      ),
    )
  })

  it('sends an explicit tier only when it was overridden', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('button', { name: 'override → low' }))
    await userEvent.type(screen.getByLabelText('Goal'), 'restart the gateway and check health')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body?.modelLevel).toBe('Low')
  })

  it('prefills the path from the files view, as a goal and a scope lease', async () => {
    // The scope is what stops two delegates racing on the same file in a shared workspace.
    const captured = captureCreate()
    renderWithProviders(
      <DelegateModal
        opened
        onClose={() => {}}
        prefill={{ goal: 'In docs/setup.md: ', workingDirectory: 'C:/src/antiphon', scopeGlob: 'docs/setup.md' }}
      />,
    )

    const goal = screen.getByLabelText('Goal')
    expect(goal).toHaveValue('In docs/setup.md: ')

    await userEvent.type(goal, 'say pwsh 7 instead of cmd')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({
      goal: 'In docs/setup.md: say pwsh 7 instead of cmd',
      workingDirectory: 'C:/src/antiphon',
      scopeGlob: 'docs/setup.md',
    })
  })

  it('will not delegate an empty goal', async () => {
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect(screen.getByRole('button', { name: 'Delegate' })).toBeDisabled()
  })
})
