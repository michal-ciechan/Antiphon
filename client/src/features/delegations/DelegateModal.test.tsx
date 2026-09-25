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
  scope: string | null
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
  it('omits runner and platform unless the caller chooses them, and keeps a conflicting runner', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} prefill={{ cardDefaultPlatform: 'Windows' }} />)
    await userEvent.type(screen.getByLabelText('Goal'), 'stay automatic')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))
    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).not.toHaveProperty('runnerId')
    expect(captured.body).not.toHaveProperty('requiredPlatform')

    const conflicted = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    await userEvent.type(screen.getAllByLabelText('Goal')[1], 'windows on linux')
    const platforms = screen.getAllByTestId('delegate-platform')
    await userEvent.click(platforms[platforms.length - 1])
    await userEvent.click(await screen.findByRole('option', { name: 'Windows' }))
    const runners = screen.getAllByTestId('delegate-runner')
    await userEvent.click(runners[runners.length - 1])
    await userEvent.click(await screen.findByRole('option', { name: /server2/ }))
    expect(screen.getByTestId('delegate-platform-conflict')).toBeInTheDocument()
    const delegateButtons = screen.getAllByRole('button', { name: 'Delegate' })
    expect(delegateButtons[delegateButtons.length - 1]).toBeDisabled()
    expect(conflicted.body).toBeNull()
  })

  it('C470 submits selected mutation role', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} prefill={{ workingDirectory: 'C:/worktrees/card-task-aabbccdd' }} />)
    await userEvent.click(screen.getByRole('radio', { name: 'Mutation' }))
    await userEvent.type(screen.getByLabelText('Goal'), 'run planned positive controls')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))
    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ role: 'Mutation', workspace: 'Worktree', workingDirectory: 'C:/worktrees/card-task-aabbccdd' })
  })

  it('defaults to a worker and leaves the tier to the role', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.type(screen.getByLabelText('Goal'), 'rename the install section')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ kind: 'Worker', workspace: 'Worktree', modelLevel: null })
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
        prefill={{ goal: 'In docs/setup.md: ', workingDirectory: 'C:/src/antiphon', scope: 'docs/setup.md' }}
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
      scope: 'docs/setup.md',
    })
  })

  it('defaults to Worktree', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect(screen.getByRole('radio', { name: 'Worktree' })).toBeChecked()

    await userEvent.type(screen.getByLabelText('Goal'), 'rename the install section')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ kind: 'Worker', role: 'Code', workspace: 'Worktree' })
  })

  it('keeps explicit Shared across kind changes', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Shared' }))
    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))

    expect(screen.getByRole('radio', { name: 'Shared' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Plan' })).toBeChecked()

    await userEvent.type(screen.getByLabelText('Goal'), 'orchestrate in the caller directory')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ kind: 'Orchestrator', role: 'Plan', workspace: 'Shared' })
  })

  it('submits explicit ReadOnly', async () => {
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Read-only' }))
    await userEvent.type(screen.getByLabelText('Goal'), 'read the landing notes')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ kind: 'Worker', workspace: 'ReadOnly' })
  })

  it('will not delegate an empty goal', async () => {
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect(screen.getByRole('button', { name: 'Delegate' })).toBeDisabled()
  })

  it('lists runners only from the catalogue', async () => {
    server.use(http.get('/api/session-runners', () => HttpResponse.json([runnerRow('server2', 'linux')])))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    await userEvent.click(screen.getByTestId('delegate-runner'))
    expect(await screen.findByRole('option', { name: /server2/ })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /^Desktop/ })).not.toBeInTheDocument()
  })

  it('reads a conflict from the catalogue platform', async () => {
    server.use(http.get('/api/session-runners', () => HttpResponse.json([
      runnerRow('desktop', 'linux', 'Desktop'),
    ])))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    await userEvent.click(screen.getByTestId('delegate-platform'))
    await userEvent.click(await screen.findByRole('option', { name: 'Windows' }))
    await userEvent.click(screen.getByTestId('delegate-runner'))
    const desktops = await screen.findAllByRole('option', { name: /^Desktop/ })
    expect(desktops).toHaveLength(1)
    expect(desktops[0]).toHaveTextContent('linux')
    await userEvent.click(desktops[0])
    expect(screen.getByTestId('delegate-platform-conflict')).toHaveTextContent('linux')
    expect(screen.getByRole('button', { name: 'Delegate' })).toBeDisabled()
  })

  it('treats an unknown catalogue platform as no conflict', async () => {
    server.use(http.get('/api/session-runners', () => HttpResponse.json([
      runnerRow('desktop', null, 'Desktop'),
    ])))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    await userEvent.click(screen.getByTestId('delegate-platform'))
    await userEvent.click(await screen.findByRole('option', { name: 'Windows' }))
    await userEvent.click(screen.getByTestId('delegate-runner'))
    await userEvent.click(await screen.findByRole('option', { name: /platform unknown/ }))
    expect(screen.queryByTestId('delegate-platform-conflict')).not.toBeInTheDocument()
  })

  it('shows a stale create 409 and keeps the draft open', async () => {
    const { notifications } = await import('@mantine/notifications')
    server.use(http.post('/api/agent-tasks', () => HttpResponse.json(
      { title: 'concurrency_conflict', detail: 'The card changed. Reload and try again.' },
      { status: 409 },
    )))
    const onClose = vi.fn()
    renderWithProviders(<DelegateModal opened onClose={onClose} />)
    await userEvent.type(screen.getByLabelText('Goal'), 'keep this draft')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))
    await waitFor(() => expect(notifications.show).toHaveBeenCalledWith(
      expect.objectContaining({ color: 'red', message: expect.stringContaining('card changed') }),
    ))
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Goal')).toHaveValue('keep this draft')
  })
})

function runnerRow(id: string, platform: string | null, displayName = id) {
  return {
    runnerId: id,
    displayName,
    platform,
    platformObservedAt: null,
    available: true,
    dispatchEligible: true,
    unavailableReason: null,
    capacity: 4,
    occupied: 0,
    capacityKind: 'sessions',
    capacityObservedAt: null,
    stale: false,
    features: ['required-platform-v1'],
  }
}
