import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { CardDto } from '../../api/boards'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentPicker } from './AgentPicker'
import { CardModal } from './CardModal'

const card: CardDto = {
  id: 'card-1',
  boardId: 'board-1',
  boardColumnId: 'column-backlog',
  ownerSessionId: null,
  currentWorktreeId: null,
  identifier: 'CARD-0001',
  title: 'Implement terminal',
  description: 'Wire xterm to the session stream',
  priority: 2,
  labels: ['ui'],
  status: 'Backlog',
  concurrencyToken: 'token-1',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  startedAt: null,
  completedAt: null,
  terminalReason: null,
  sessions: [],
}

function agentHandlers() {
  return http.get('/api/agents', () =>
    HttpResponse.json({
      defaultDefinition: 'claude',
      definitions: [
        { name: 'claude', kind: 'ClaudeCode', isDefault: true },
        { name: 'raw', kind: 'Raw', isDefault: false },
      ],
    }),
  )
}

function getAgentInput() {
  return screen
    .getAllByLabelText('Agent')
    .find((element): element is HTMLInputElement =>
      element instanceof HTMLInputElement && element.getAttribute('type') !== 'hidden',
    ) as HTMLInputElement
}

describe('AgentPicker', () => {
  it('renders configured registry options and selects the default', async () => {
    const onChange = vi.fn()
    server.use(agentHandlers())

    renderWithProviders(<AgentPicker value={null} onChange={onChange} />)

    await waitFor(() => expect(onChange).toHaveBeenCalledWith('claude'))
    await userEvent.click(getAgentInput())
    expect(await screen.findByText('claude (ClaudeCode, default)')).toBeInTheDocument()
    expect(screen.getByText('raw (Raw)')).toBeInTheDocument()
  })
})

describe('CardModal', () => {
  it('posts spawn with the selected agent definition', async () => {
    const spawnSpy = vi.fn()
    server.use(
      agentHandlers(),
      http.post('/api/cards/card-1/spawn', async ({ request }) => {
        spawnSpy(await request.json())
        return HttpResponse.json({ cardId: 'card-1', sessionId: 'session-1' }, { status: 202 })
      }),
    )

    renderWithProviders(
      <CardModal boardId="board-1" card={card} opened onClose={() => undefined} />,
    )

    await waitFor(() => expect(getAgentInput()).toHaveValue('claude (ClaudeCode, default)'))
    await userEvent.click(screen.getByRole('button', { name: 'Spawn' }))

    await waitFor(() => expect(spawnSpy).toHaveBeenCalledWith({
      definitionName: 'claude',
      cols: 120,
      rows: 30,
    }))
  })
})
