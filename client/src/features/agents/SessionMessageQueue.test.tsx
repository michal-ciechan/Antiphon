import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SessionMessageQueue } from './SessionMessageQueue'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

// The live half is SignalR; this file is about what the HTTP shape RENDERS, so the hub is stubbed
// to a connection that never connects and never pushes.
vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        state: 'Disconnected',
        on: vi.fn(),
        off: vi.fn(),
        onreconnected: vi.fn(),
        start: () => Promise.reject(new Error('no hub in tests')),
        invoke: vi.fn(),
        stop: vi.fn(),
      }
    }
  },
}))

// The composer pulls its own slash-command catalog; it is not what this file is about.
vi.mock('./SmartComposer', () => ({ SmartComposer: () => <div data-testid="composer" /> }))

function message(overrides: Record<string, unknown> = {}) {
  return {
    id: 'm1',
    sequence: 1,
    body: 'the guest list, as asked',
    status: 'Pending',
    createdAt: '2026-08-17T09:00:00Z',
    deliveryAttempts: 0,
    origin: 'Ui',
    parked: false,
    ...overrides,
  }
}

function serve(messages: unknown[]) {
  server.use(
    http.get('/api/sessions/s1/messages', () =>
      HttpResponse.json({ sessionId: 's1', messages, working: false }),
    ),
  )
}

describe('SessionMessageQueue', () => {
  it('marks a parked message, because Pending alone does not say it is going nowhere', async () => {
    // CARD-0055 shipped parking and nothing rendered it: a message that spent its delivery attempts
    // stays Pending, sits in this list looking exactly like one about to go out, and every automatic
    // path has already excluded it. Without the chip the only honest reading of this queue is wrong.
    serve([message({ parked: true, deliveryAttempts: 3, origin: 'Channel' })])

    renderWithProviders(<SessionMessageQueue sessionId="s1" />)

    expect(await screen.findByText('Parked')).toBeInTheDocument()
    expect(screen.getByText('the guest list, as asked')).toBeInTheDocument()
  })

  it('shows the scheduled badge from the note header', async () => {
    serve([
      message({
        origin: 'Scheduled',
        noteHeader: 'Scheduled · Morning triage',
        body: '[scheduled: Morning triage]\nhello',
      }),
    ])

    renderWithProviders(<SessionMessageQueue sessionId="s1" />)

    expect(await screen.findByTestId('scheduled-badge')).toHaveTextContent('Scheduled · Morning triage')
  })

  it('leaves an ordinary pending message unmarked', async () => {
    serve([message({})])

    renderWithProviders(<SessionMessageQueue sessionId="s1" />)

    await waitFor(() =>
      expect(screen.getByText('the guest list, as asked')).toBeInTheDocument(),
    )
    expect(screen.queryByText('Parked')).not.toBeInTheDocument()
  })
})
