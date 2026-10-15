import { onlineManager } from '@tanstack/react-query'
import { HttpResponse, http } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AgentTaskRole } from '../../api/agentTasks'
import type {
  ComplexityCandidateDto,
  ComplexityChainDto,
  ComplexityResolvedFrom,
  TaskComplexity,
} from '../../api/complexityChains'
import { cleanup } from '@testing-library/react'
import { act, renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ComplexityChainCellEditor } from './ComplexityChainCellEditor'
import {
  CHAIN_CLEAR_SUCCESS,
  CHAIN_QUEUED_NOTE,
  CHAIN_SAVE_SUCCESS,
  CHAIN_STALE_TITLE,
  INHERITED_REPLACE_WARNING,
  OVERWRITE_UNREAD_METADATA,
} from './routingSettingsModel'

const OPENED_TOKEN = '7b0e2c1a-0000-4000-8000-0000000000aa'
const CURRENT_TOKEN = '7b0e2c1a-0000-4000-8000-0000000000bb'

const notificationMock = vi.hoisted(() => ({ show: vi.fn() }))

vi.mock('@mantine/notifications', () => ({
  notifications: notificationMock,
}))

const grok: ComplexityCandidateDto = {
  agentKind: 'Grok',
  modelLevel: 'Frontier',
  alias: 'grok-4.7',
  availableNow: true,
  unavailableReason: null,
}

const codex: ComplexityCandidateDto = {
  agentKind: 'Codex',
  modelLevel: 'Frontier',
  alias: 'gpt-6-astra',
  availableNow: true,
  unavailableReason: null,
}

const claude: ComplexityCandidateDto = {
  agentKind: 'ClaudeCode',
  modelLevel: 'Frontier',
  alias: 'fable',
  availableNow: false,
  unavailableReason: 'held',
}

function chain(overrides: Partial<ComplexityChainDto> = {}): ComplexityChainDto {
  return {
    complexity: 'Hard',
    candidates: [codex],
    provenance: 'Human',
    source: 'pin',
    reason: 'Plan cell',
    notAfter: null,
    updatedAt: '2026-09-04T00:00:00Z',
    role: 'Plan',
    resolvedFrom: 'role',
    concurrencyToken: OPENED_TOKEN,
    ...overrides,
  }
}

function renderEditor(options?: {
  role?: AgentTaskRole | null
  complexity?: TaskComplexity
  chain?: ComplexityChainDto
  isAnyRoleRow?: boolean
  fallbackResolvedFrom?: ComplexityResolvedFrom
  onClose?: () => void
}) {
  const onClose = options?.onClose ?? vi.fn()
  renderWithProviders(
    <ComplexityChainCellEditor
      opened
      onClose={onClose}
      role={options?.role === undefined ? 'Plan' : options.role}
      complexity={options?.complexity ?? 'Hard'}
      chain={options?.chain ?? chain()}
      isAnyRoleRow={options?.isAnyRoleRow ?? false}
      fallbackResolvedFrom={options?.fallbackResolvedFrom ?? 'any'}
    />,
  )
  return { onClose }
}

function forbidSideEffects() {
  const forbidden = vi.fn()
  server.use(
    http.put('/api/model-availability/:kind/:alias', () => {
      forbidden('availability-put')
      return HttpResponse.json({ title: 'should not run' }, { status: 500 })
    }),
    http.delete('/api/model-availability/:kind/:alias', () => {
      forbidden('availability-delete')
      return HttpResponse.json({ title: 'should not run' }, { status: 500 })
    }),
    http.put('/api/routing-pins', () => {
      forbidden('pin-put')
      return HttpResponse.json({ title: 'should not run' }, { status: 500 })
    }),
    http.delete('/api/routing-pins/:id', () => {
      forbidden('pin-delete')
      return HttpResponse.json({ title: 'should not run' }, { status: 500 })
    }),
    http.post('/api/agent-tasks', () => {
      forbidden('dispatch')
      return HttpResponse.json({ title: 'should not run' }, { status: 500 })
    }),
    http.post('/api/subscription-usage', () => {
      forbidden('usage-post')
      return HttpResponse.json({ title: 'should not run' }, { status: 500 })
    }),
  )
  return forbidden
}

describe('ComplexityChainCellEditor', () => {
  beforeEach(() => {
    notificationMock.show.mockReset()
  })

  it('adds, removes, and reorders candidates with accessible controls', async () => {
    renderEditor({
      chain: chain({
        role: 'Code',
        candidates: [claude, grok],
      }),
    })

    expect(screen.getByTestId('routing-cell-editor-candidate-0')).toHaveAttribute(
      'data-agent-kind',
      'ClaudeCode',
    )
    expect(screen.getByTestId('routing-cell-editor-candidate-1')).toHaveAttribute(
      'data-agent-kind',
      'Grok',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Move candidate 2 up' }))
    expect(screen.getByTestId('routing-cell-editor-candidate-0')).toHaveAttribute(
      'data-agent-kind',
      'Grok',
    )
    expect(screen.getByTestId('routing-cell-editor-candidate-1')).toHaveAttribute(
      'data-agent-kind',
      'ClaudeCode',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Move candidate 1 down' }))
    expect(screen.getByTestId('routing-cell-editor-candidate-0')).toHaveAttribute(
      'data-agent-kind',
      'ClaudeCode',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Add candidate' }))
    expect(screen.getByTestId('routing-cell-editor-candidate-2')).toHaveAttribute(
      'data-agent-kind',
      'ClaudeCode',
    )
    expect(screen.getByTestId('routing-cell-editor-candidate-2')).toHaveAttribute(
      'data-model-level',
      'High',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Remove candidate 2' }))
    expect(screen.queryByTestId('routing-cell-editor-candidate-2')).not.toBeInTheDocument()
    expect(screen.getByTestId('routing-cell-editor-candidate-1')).toHaveAttribute(
      'data-agent-kind',
      'ClaudeCode',
    )
  })

  it('PUTs the exact Human wire payload and shows the D6 success copy', async () => {
    const putSpy = vi.fn()
    const forbidden = forbidSideEffects()
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', async ({ request }) => {
        putSpy(new URL(request.url).pathname, await request.json())
        return HttpResponse.json(chain())
      }),
    )
    const { onClose } = renderEditor()

    const reason = screen.getByLabelText('Reason (optional)')
    await userEvent.clear(reason)
    await userEvent.type(reason, 'plan-grade')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(putSpy).toHaveBeenCalled())
    expect(putSpy).toHaveBeenCalledWith('/api/complexity-chains/Plan/Hard', {
      candidates: [{ agentKind: 'Codex', modelLevel: 'Frontier' }],
      provenance: 'Human',
      reason: 'plan-grade',
      notAfter: null,
      concurrencyToken: OPENED_TOKEN,
    })
    expect(notificationMock.show).toHaveBeenCalledWith({
      color: 'green',
      message: CHAIN_SAVE_SUCCESS,
    })
    expect(CHAIN_SAVE_SUCCESS).toMatch(/New complexity-routed dispatches/)
    expect(CHAIN_SAVE_SUCCESS).toMatch(/queued/)
    expect(CHAIN_SAVE_SUCCESS).toMatch(/Running sessions keep the model they started with/)
    expect(onClose).toHaveBeenCalled()
    expect(forbidden).not.toHaveBeenCalled()
  })

  it('PUTs the any-role cell to /complexity-chains/any/Hard', async () => {
    const putSpy = vi.fn()
    server.use(
      http.put('/api/complexity-chains/any/Hard', async ({ request }) => {
        putSpy(new URL(request.url).pathname, await request.json())
        return HttpResponse.json(chain({ role: null, resolvedFrom: 'any', candidates: [grok] }))
      }),
    )
    renderEditor({
      role: null,
      isAnyRoleRow: true,
      chain: chain({
        role: null,
        resolvedFrom: 'any',
        candidates: [grok],
        reason: 'any-role Hard',
      }),
    })

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(putSpy).toHaveBeenCalled())
    expect(putSpy).toHaveBeenCalledWith('/api/complexity-chains/any/Hard', {
      candidates: [{ agentKind: 'Grok', modelLevel: 'Frontier' }],
      provenance: 'Human',
      reason: 'any-role Hard',
      notAfter: null,
      concurrencyToken: OPENED_TOKEN,
    })
  })

  it('asks for source-specific confirmation before DELETE of an Any-role row', async () => {
    const deleteSpy = vi.fn()
    const forbidden = forbidSideEffects()
    server.use(
      http.delete('/api/complexity-chains/any/Hard', ({ request }) => {
        deleteSpy(new URL(request.url).pathname)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderEditor({
      role: null,
      isAnyRoleRow: true,
      fallbackResolvedFrom: 'none',
      chain: chain({
        role: null,
        resolvedFrom: 'any',
        candidates: [grok],
        reason: 'any-role Hard',
      }),
    })

    await userEvent.click(screen.getByRole('button', { name: 'Clear override' }))
    const confirm = await screen.findByTestId('routing-cell-editor-clear-confirm')
    expect(within(confirm).getByText(/Clear Any role Hard fallback/)).toBeInTheDocument()
    expect(within(confirm).getByText(/removes the fallback used by every role/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Confirm clear' }))
    await waitFor(() => expect(deleteSpy).toHaveBeenCalledWith('/api/complexity-chains/any/Hard'))
    expect(notificationMock.show).toHaveBeenCalledWith({
      color: 'green',
      message: CHAIN_CLEAR_SUCCESS,
    })
    expect(CHAIN_CLEAR_SUCCESS).toMatch(/Running sessions keep the model they started with/)
    expect(forbidden).not.toHaveBeenCalled()
  })

  it('confirms a role-cell clear as fallback to Any role, then DELETE', async () => {
    const deleteSpy = vi.fn()
    server.use(
      http.delete('/api/complexity-chains/Plan/Hard', ({ request }) => {
        deleteSpy(new URL(request.url).pathname)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderEditor({ fallbackResolvedFrom: 'any' })

    await userEvent.click(screen.getByRole('button', { name: 'Clear override' }))
    expect(await screen.findByText(/falls back to the Any role list/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Confirm clear' }))
    await waitFor(() => expect(deleteSpy).toHaveBeenCalledWith('/api/complexity-chains/Plan/Hard'))
  })

  it('confirms a blocking clear when there is no fallback', async () => {
    renderEditor({ fallbackResolvedFrom: 'none' })
    await userEvent.click(screen.getByRole('button', { name: 'Clear override' }))
    expect(await screen.findByText(/leaves this cell Unset/)).toBeInTheDocument()
    expect(screen.getByText(/will block/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('shows inherited replace-as-a-whole copy and hides Clear', async () => {
    renderEditor({
      chain: chain({
        resolvedFrom: 'any',
        source: 'pin',
        provenance: 'Human',
        candidates: [grok],
        reason: 'any-role Hard',
      }),
      isAnyRoleRow: false,
      fallbackResolvedFrom: 'any',
    })

    expect(screen.getByText(INHERITED_REPLACE_WARNING)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear override' })).not.toBeInTheDocument()
  })

  it('surfaces a 422 Problem Details error in the form and returns focus to Save', async () => {
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', () =>
        HttpResponse.json(
          {
            title: 'Unprocessable Entity',
            status: 422,
            detail: 'One or more validation errors occurred.',
            errors: {
              Candidates: ['A chain needs 1 to 8 candidates. An empty list is a DELETE, not a PUT.'],
            },
          },
          { status: 422 },
        ),
      ),
    )
    renderEditor()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(
      await screen.findByText('A chain needs 1 to 8 candidates. An empty list is a DELETE, not a PUT.'),
    ).toBeInTheDocument()
    expect(screen.getByTestId('routing-cell-editor-candidates')).toHaveTextContent(
      'A chain needs 1 to 8 candidates. An empty list is a DELETE, not a PUT.',
    )
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toHaveFocus())
  })

  it('surfaces a 409 Problem Details conflict in the form and returns focus to Save', async () => {
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            status: 409,
            code: 'complexity_chain_human',
            detail:
              'The Plan/Hard chain was set by a human ("plan-grade"). An automatic decision cannot overwrite it.',
          },
          { status: 409 },
        ),
      ),
    )
    renderEditor()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(
      await screen.findByText(/The Plan\/Hard chain was set by a human/),
    ).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toHaveFocus())
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })

  it('returns focus to Confirm clear when DELETE returns 409', async () => {
    server.use(
      http.delete('/api/complexity-chains/Plan/Hard', () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            status: 409,
            detail: 'The Plan/Hard chain is in use by another write.',
          },
          { status: 409 },
        ),
      ),
    )
    renderEditor()

    await userEvent.click(screen.getByRole('button', { name: 'Clear override' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm clear' }))
    expect(await screen.findByText('The Plan/Hard chain is in use by another write.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirm clear' })).toHaveFocus())
  })
})

describe('C1275 editor concurrency token', () => {
  beforeEach(() => {
    notificationMock.show.mockReset()
  })
  afterEach(() => {
    onlineManager.setOnline(true)
  })

  const staleBody = (current: ComplexityChainDto) => ({
    title: 'Conflict',
    status: 409,
    code: 'complexity_chain_stale',
    detail: 'The Plan/Hard chain changed since it was read.',
    current,
  })

  it('C1275 a stale Save shows the stored row, does not retry, and Reload adopts the new version only on click', async () => {
    const bodies: unknown[] = []
    const current = chain({ candidates: [grok], reason: 'other operator', concurrencyToken: CURRENT_TOKEN })
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', async ({ request }) => {
        bodies.push(await request.json())
        return bodies.length === 1
          ? HttpResponse.json(staleBody(current), { status: 409 })
          : HttpResponse.json(current)
      }),
    )
    const { onClose } = renderEditor()

    await userEvent.type(screen.getByLabelText('Reason (optional)'), ' mine')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    const stale = await screen.findByTestId('routing-cell-editor-stale')
    expect(within(stale).getByText(CHAIN_STALE_TITLE)).toBeInTheDocument()
    expect(stale).toHaveTextContent('Stored now: Grok/Frontier')
    expect(stale).toHaveTextContent('reason "other operator"')
    expect(stale).toHaveTextContent('Your draft: Codex/Frontier')
    expect(stale).toHaveTextContent('reason "Plan cell mine"')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Clear override' })).toBeDisabled()
    expect(bodies).toHaveLength(1)
    expect(onClose).not.toHaveBeenCalled()
    // The draft is untouched until the operator asks for the stored row.
    expect(screen.getByLabelText('Reason (optional)')).toHaveValue('Plan cell mine')

    await userEvent.click(within(stale).getByRole('button', { name: 'Reload' }))
    expect(screen.queryByTestId('routing-cell-editor-stale')).not.toBeInTheDocument()
    expect(screen.getByTestId('routing-cell-editor-candidate-0')).toHaveAttribute('data-agent-kind', 'Grok')
    expect(screen.getByLabelText('Reason (optional)')).toHaveValue('other operator')
    expect(bodies).toHaveLength(1)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(bodies).toEqual([
      {
        candidates: [{ agentKind: 'Codex', modelLevel: 'Frontier' }],
        provenance: 'Human',
        reason: 'Plan cell mine',
        notAfter: null,
        concurrencyToken: OPENED_TOKEN,
      },
      {
        candidates: [{ agentKind: 'Grok', modelLevel: 'Frontier' }],
        provenance: 'Human',
        reason: 'other operator',
        notAfter: null,
        concurrencyToken: CURRENT_TOKEN,
      },
    ])
  })

  it('C1275 a stale Clear sends the opened token and shows that the row is now absent', async () => {
    const searches: string[] = []
    const current = chain({ candidates: [], provenance: null, source: 'config', reason: null, resolvedFrom: 'none', concurrencyToken: null })
    server.use(
      http.delete('/api/complexity-chains/Plan/Hard', ({ request }) => {
        searches.push(new URL(request.url).search)
        return HttpResponse.json(staleBody(current), { status: 409 })
      }),
    )
    renderEditor({ fallbackResolvedFrom: 'any' })

    await userEvent.click(screen.getByRole('button', { name: 'Clear override' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm clear' }))
    const stale = await screen.findByTestId('routing-cell-editor-stale')
    expect(stale).toHaveTextContent(CHAIN_STALE_TITLE)
    expect(stale).toHaveTextContent('Stored now: no own row for this cell')
    expect(searches).toEqual([`?concurrencyToken=${OPENED_TOKEN}`])
  })

  it('C1275 an unset cell sends the empty token; an unread cell confirmed for overwrite sends none', async () => {
    const bodies: Record<string, unknown>[] = []
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', async ({ request }) => {
        bodies.push((await request.json()) as Record<string, unknown>)
        return HttpResponse.json(chain())
      }),
    )
    const unset = renderEditor({
      chain: chain({ candidates: [], provenance: null, source: 'config', reason: null, resolvedFrom: 'none', concurrencyToken: null }),
      fallbackResolvedFrom: 'none',
    })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(unset.onClose).toHaveBeenCalled())
    expect(bodies[0].concurrencyToken).toBe('00000000-0000-0000-0000-000000000000')
    cleanup()

    const unread = renderEditor({
      chain: chain({
        concurrencyToken: null,
        effective: {
          source: 'role',
          candidates: [codex],
          stagePin: null,
          governsDefault: false,
          withheldReason: 'chain metadata read failed (TimeoutException)',
        },
      }),
    })
    await userEvent.click(screen.getByLabelText(OVERWRITE_UNREAD_METADATA))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(unread.onClose).toHaveBeenCalled())
    expect(bodies).toHaveLength(2)
    expect('concurrencyToken' in bodies[1]).toBe(false)
  })

  it('C1275 an offline Save shows queued and sends once with the opened token when back online', async () => {
    const bodies: unknown[] = []
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(chain())
      }),
    )
    const { onClose } = renderEditor()
    onlineManager.setOnline(false)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByTestId('routing-cell-editor-queued')).toHaveTextContent(CHAIN_QUEUED_NOTE)
    expect(bodies).toHaveLength(0)
    expect(onClose).not.toHaveBeenCalled()

    act(() => { onlineManager.setOnline(true) })
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(bodies).toHaveLength(1)
    expect((bodies[0] as Record<string, unknown>).concurrencyToken).toBe(OPENED_TOKEN)
  })
})
