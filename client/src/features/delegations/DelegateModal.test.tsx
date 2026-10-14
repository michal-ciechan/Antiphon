import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ROLE_POLICY_FIXTURE } from '../../test/mocks/handlers'
import type { RoutingPinCandidateDto, RoutingPinDto } from '../../api/routingPins'
import type { RolePolicyRowDto } from '../../api/rolePolicy'
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

  it('C097 V16 server-level-preselects', async () => {
    serveRolePolicyRow('Mutation', (row) => ({
      ...row,
      level: 'High',
      alias: 'opus',
      effective: { ...row.effective, modelLevel: 'High', alias: 'opus' },
    }))
    const captured = captureCreate()
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Mutation' }))
    expect(await screen.findByTestId('delegate-tier-source-rolePolicy')).toBeInTheDocument()
    expect(screen.getByTestId('tier-High')).toBeInTheDocument()
    expect(screen.queryByTestId('tier-Low')).not.toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Goal'), 'run the positive controls')
    await userEvent.click(screen.getByRole('button', { name: 'Delegate' }))
    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body).toMatchObject({ role: 'Mutation', modelLevel: null })
  })

  it('C097 V17 stage-pin-wins', async () => {
    serveRolePolicyRow('Plan', (row) => ({
      ...row,
      level: 'Frontier',
      stagePin: previewPin([candidate('ClaudeCode', 'High', 'opus')]),
      effective: { source: 'pin', agentKind: 'ClaudeCode', modelLevel: 'High', alias: 'opus', strength: 'Required' },
    }))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Plan' }))
    expect(await screen.findByTestId('delegate-tier-source-pin')).toBeInTheDocument()
    expect(screen.getByTestId('tier-High')).toBeInTheDocument()
    expect(screen.queryByTestId('tier-Frontier')).not.toBeInTheDocument()
  })

  it('C097 V18 route-failure-discloses-unknown', async () => {
    server.use(http.get('/api/role-policy', () => new HttpResponse(null, { status: 500 })))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Mutation' }))
    expect((await screen.findByTestId('delegate-tier-source-unknown')).textContent).toBe('unknown — role policy snapshot depends on the server')
    expect(screen.queryByTestId('tier-Low')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Goal')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Goal'), 'run the positive controls')
    expect(screen.getByRole('button', { name: 'Delegate' })).toBeEnabled()
  })

  it('C097 V30 undefined-policy-level-falls-back-to-constant', async () => {
    serveRolePolicyRow('Mutation', (row) => ({
      ...row,
      level: null,
      alias: '',
      effective: { source: 'rolePolicy', agentKind: null, modelLevel: null, alias: null, strength: null },
    }))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Mutation' }))
    expect(await screen.findByTestId('delegate-tier-source-fallback')).toBeInTheDocument()
    expect(screen.queryByTestId('delegate-tier-source-rolePolicy')).not.toBeInTheDocument()
    expect(screen.getByTestId('tier-Low')).toBeInTheDocument()
  })

  it('C1234 unsupported-policy-kind-falls-back-to-constant', async () => {
    // The row keeps its defined level; the server withholds the effective target (Kind Raw).
    serveRolePolicyRow('Mutation', (row) => ({
      ...row,
      level: 'High',
      kind: 'Raw',
      alias: '',
      effective: { source: 'rolePolicy', agentKind: null, modelLevel: null, alias: null, strength: null },
    }))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Mutation' }))
    expect(await screen.findByTestId('delegate-tier-source-fallback')).toBeInTheDocument()
    expect(screen.queryByTestId('delegate-tier-source-rolePolicy')).not.toBeInTheDocument()
    expect(screen.getByTestId('tier-Low')).toBeInTheDocument()
    expect(screen.queryByTestId('tier-High')).not.toBeInTheDocument()
  })

  it('C097 V27 orchestrator-floor-is-a-server-setting', async () => {
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    await userEvent.click(screen.getByRole('radio', { name: 'Mutation' }))
    expect((await screen.findByTestId('delegate-tier-source-unknown')).textContent).toBe('unknown — Orchestrator level depends on the server floor')
    expect(screen.queryByTestId('tier-High')).not.toBeInTheDocument()
    expect(screen.queryByTestId('tier-Low')).not.toBeInTheDocument()
  })

  it('C1250 V13 worker-preview-reads-the-default-tier-row-and-orchestrator-does-not', async () => {
    serveCodeRow('Medium', [held('Codex', 'Frontier', 'gpt-6-astra'), candidate('ClaudeCode', 'High', 'opus')])
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect((await screen.findByTestId('delegate-tier-source-matrix')).textContent).toBe(
      'from the Medium row, skipping held gpt-6-astra',
    )
    expect(screen.getByTestId('tier-High')).toBeInTheDocument()
    expect(screen.queryByTestId('tier-Frontier')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    await userEvent.click(screen.getByRole('radio', { name: 'Code' }))
    expect(await screen.findByTestId('delegate-tier-source-rolePolicy')).toBeInTheDocument()
    expect(screen.getByTestId('tier-Frontier')).toBeInTheDocument()
    expect(screen.queryByTestId('delegate-tier-source-matrix')).not.toBeInTheDocument()
  })

  it('C1250 V14 non-medium-default-tier-row', async () => {
    serveCodeRow('Easy', [candidate('Codex', 'Low', 'gpt-5.6-luna')])
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect((await screen.findByTestId('delegate-tier-source-matrix')).textContent).toBe('from the Easy row')
    expect(screen.getByTestId('tier-Low')).toBeInTheDocument()
  })

  it('C1250 V15 exhausted-row-says-blocked', async () => {
    serveCodeRow('Medium', [held('Codex', 'High', 'gpt-6.1-sol'), held('ClaudeCode', 'High', 'opus')])
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect((await screen.findByTestId('delegate-tier-source-exhausted')).textContent).toBe(
      'every Medium-row candidate is held; the create would be Blocked',
    )
    expect(screen.getByTestId('tier-High')).toBeInTheDocument()
  })

  it('C1250 V18 single-held-pin-shows-refusal', async () => {
    serveRolePolicyRow('Code', (row) => ({ ...row, stagePin: previewPin([held('Codex', 'Frontier', 'gpt-6-astra')]) }))
    serveCodeRow('Medium', [held('Codex', 'Frontier', 'gpt-6-astra')], 'pin')
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    expect((await screen.findByTestId('delegate-tier-source-refused')).textContent).toBe(
      'create refused (409): gpt-6-astra is held; model_disabled or subscription_quota_low depends on the server',
    )
    expect(screen.queryByTestId('delegate-tier-source-exhausted')).not.toBeInTheDocument()
    expect(screen.queryByText('Runs at')).not.toBeInTheDocument()
  })

  it.each([
    { state: 'held', availableNow: false, unavailableReason: 'held (manual)', forbiddenAliases: ['unrelated-alias'], label: 'create refused (409): gpt-6-astra is held; model_disabled or subscription_quota_low depends on the server' },
    { state: 'quota', availableNow: false, unavailableReason: 'quota 2% remaining, resets in 10m', forbiddenAliases: ['unrelated-alias'], label: 'create refused (409 subscription_quota_low): gpt-6-astra is quota-limited' },
    { state: 'forbidden', availableNow: true, unavailableReason: null, forbiddenAliases: ['GPT-6-ASTRA'], label: 'create refused (409 routing_pin_forbidden): gpt-6-astra is forbidden by the stage pin' },
  ])('C1250 V21 required-forbid-only-single-row-$state-is-refused', async ({ availableNow, unavailableReason, forbiddenAliases, label }) => {
    serveRolePolicyRow('Code', (row) => ({ ...row, stagePin: { ...previewPin([]), forbiddenAliases } }))
    serveCodeRow('Medium', [{ ...candidate('Codex', 'Frontier', 'gpt-6-astra'), availableNow, unavailableReason }])
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    expect((await screen.findByTestId('delegate-tier-source-refused')).textContent).toBe(label)
    expect(screen.queryByTestId('delegate-tier-source-exhausted')).not.toBeInTheDocument()
    expect(screen.queryByText('Runs at')).not.toBeInTheDocument()
  })

  it('C1250 V19 walked-orchestrator-medium-is-not-floored', async () => {
    serveRolePolicyRow('Plan', (row) => ({ ...row, stagePin: previewPin([candidate('ClaudeCode', 'Medium', 'sonnet'), candidate('ClaudeCode', 'High', 'opus')]) }))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    await userEvent.click(screen.getByRole('radio', { name: 'Sub-orchestrator' }))
    expect((await screen.findByTestId('delegate-tier-source-pin')).textContent).toBe('from the stage pin')
    expect(screen.getByTestId('tier-Medium')).toBeInTheDocument()
    expect(screen.queryByTestId('tier-High')).not.toBeInTheDocument()
  })

  it('C1250 V20 unknown-policy-tail-does-not-promise-a-tier', async () => {
    serveRolePolicyRow('Code', (row) => ({ ...row, stagePin: { ...previewPin([held('Codex', 'Frontier', 'gpt-6-astra')]), strength: 'Preferred' } }))
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)
    expect((await screen.findByTestId('delegate-tier-source-unknown')).textContent).toBe('unknown — candidate availability depends on the server')
    expect(screen.queryByTestId('tier-Frontier')).not.toBeInTheDocument()
    expect(screen.queryByText('Runs at')).not.toBeInTheDocument()
  })

  it('C1250 V16 agent-only-stage-pin', async () => {
    serveCodeRow('Medium', [], 'agent', 'stage pin binds standing agent Scout (Codex); an ordinary dispatch runs on that agent and does not walk this row')
    renderWithProviders(<DelegateModal opened onClose={() => {}} />)

    expect((await screen.findByTestId('delegate-tier-source-agent')).textContent).toBe(
      'stage pin binds a standing agent; its model and admission depend on the server',
    )
    expect(screen.queryByTestId('delegate-tier-source-rolePolicy')).not.toBeInTheDocument()
  })
})

function candidate(agentKind: RoutingPinCandidateDto['agentKind'], modelLevel: RoutingPinCandidateDto['modelLevel'], alias: string): RoutingPinCandidateDto {
  return { agentKind, modelLevel, alias, availableNow: true, unavailableReason: null }
}

function held(agentKind: RoutingPinCandidateDto['agentKind'], modelLevel: RoutingPinCandidateDto['modelLevel'], alias: string): RoutingPinCandidateDto {
  return { agentKind, modelLevel, alias, availableNow: false, unavailableReason: 'held (manual)' }
}

/** GET /api/complexity-chains?role=Code: one default-tier Code row; every other role resolves none. */
function serveCodeRow(
  defaultComplexity: string,
  candidates: unknown[],
  source = 'role',
  withheldReason: string | null = null,
) {
  server.use(http.get('/api/complexity-chains', ({ request }) => {
    const role = new URL(request.url).searchParams.get('role')
    return HttpResponse.json({
      defaultComplexity,
      chains: role === 'Code'
        ? [{
            complexity: defaultComplexity, candidates, provenance: 'Human', source: 'pin', reason: null,
            notAfter: null, updatedAt: null, role: 'Code', resolvedFrom: 'role',
            effective: { source, candidates, stagePin: null, governsDefault: true, withheldReason },
          }]
        : [],
    })
  }))
}

function serveRolePolicyRow(role: string, change: (row: RolePolicyRowDto) => RolePolicyRowDto) {
  server.use(http.get('/api/role-policy', () => HttpResponse.json({
    ...ROLE_POLICY_FIXTURE,
    roles: ROLE_POLICY_FIXTURE.roles.map((row) => (row.role === role ? change(row) : row)),
  })))
}

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

function previewPin(candidates: RoutingPinCandidateDto[]): RoutingPinDto {
  return { id: 'preview-pin', cardId: null, cardIdentifier: null, role: 'Code', provenance: 'Human', strength: 'Required',
    agentKind: null, modelLevel: null, modelAlias: null, agentId: null, forbiddenAliases: [], notBefore: null, notAfter: null,
    reason: 'preview parity', sourceTaskId: null, createdAt: '2026-10-14T10:00:00Z', updatedAt: '2026-10-14T10:00:00Z',
    candidates, candidateCount: candidates.length }
}
