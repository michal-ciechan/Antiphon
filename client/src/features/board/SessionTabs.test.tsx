import { HttpResponse, http } from 'msw'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { server } from '../../test/mocks/server'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { SessionTabs } from './SessionTabs'

vi.mock('./SessionTerminal', () => ({
  SessionTerminal: ({ session }: { session: AgentSessionSummaryDto }) => (
    <div data-testid="session-terminal">terminal {session.id}</div>
  ),
}))

beforeAll(() => {
  Element.prototype.scrollIntoView = vi.fn()
})

const baseSession: AgentSessionSummaryDto = {
  id: 'session-1',
  definitionName: 'claude',
  agentKind: 'ClaudeCode',
  status: 'Running',
  cwd: 'D:/repo/worktree-1',
  createdAt: '2026-01-01T00:00:00Z',
  startedAt: '2026-01-01T00:00:00Z',
  lastSeenAt: '2026-01-01T00:00:01Z',
  endedAt: null,
  exitCode: null,
  failureReason: null,
}

function getSessionSelectInput() {
  return screen
    .getAllByLabelText('All sessions')
    .find((element): element is HTMLInputElement =>
      element instanceof HTMLInputElement && element.getAttribute('type') !== 'hidden',
    ) as HTMLInputElement
}

describe('SessionTabs', () => {
  it('shows valid terminal sessions in an all-sessions dropdown and hides empty sessions', async () => {
    renderWithProviders(
      <SessionTabs
        boardId="board-1"
        sessions={[
          { ...baseSession, id: 'session-new', status: 'Running', cwd: 'D:/repo/new' },
          { ...baseSession, id: 'session-empty', status: 'Starting', cwd: '' },
          { ...baseSession, id: 'session-old', status: 'Stopped', cwd: 'D:/repo/old' },
        ]}
      />,
    )

    expect(getSessionSelectInput()).toHaveValue('Session 2 - Running')
    expect(screen.getByTestId('session-terminal')).toHaveTextContent('terminal session-new')
    expect(screen.getByTestId('hidden-session-count')).toHaveTextContent(
      'Hidden 1 preparing session without a terminal.',
    )

    await userEvent.click(getSessionSelectInput())
    await userEvent.click(await screen.findByText('Session 1 - Stopped'))

    expect(getSessionSelectInput()).toHaveValue('Session 1 - Stopped')
    expect(screen.getByTestId('session-terminal')).toHaveTextContent('terminal session-old')
  })

  it('does not render a terminal for sessions without a cwd', () => {
    renderWithProviders(
      <SessionTabs boardId="board-1" sessions={[{ ...baseSession, id: 'session-empty', status: 'Starting', cwd: '' }]} />,
    )

    expect(screen.getByText('No terminal sessions yet')).toBeInTheDocument()
    expect(screen.queryByTestId('session-terminal')).not.toBeInTheDocument()
  })

  it('shows a resume button for stopped Claude sessions and posts the stored session id', async () => {
    const postSpy = vi.fn()
    server.use(
      http.post('/api/sessions/session-old/resume', async () => {
        postSpy()
        return HttpResponse.json({ sessionId: 'session-old', cardId: 'card-1' }, { status: 202 })
      }),
    )

    renderWithProviders(
      <SessionTabs
        boardId="board-1"
        sessions={[{ ...baseSession, id: 'session-old', status: 'Stopped', cwd: 'D:/repo/old' }]}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Resume' }))

    await waitFor(() => expect(postSpy).toHaveBeenCalled())
  })
})
