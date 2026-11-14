import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SessionMessageQueue } from './SessionMessageQueue'
import userEvent from '@testing-library/user-event'

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

function serve(messages: unknown[], extras: Record<string, unknown> = {}) {
  server.use(
    http.get('/api/sessions/s1/messages', () =>
      HttpResponse.json({ sessionId: 's1', messages, working: false, ...extras }),
    ),
  )
}

describe('SessionMessageQueue', () => {
  it('shows unconfirmed Sent input with an explicit operator abandon dialog and no resend control', async () => {
    serve([message({ status: 'Sent', deliveryUnconfirmed: true, deliveryAttempts: 1,
      lastDeliveryStartedAt: '2026-11-14T10:00:00Z' })])
    renderWithProviders(<SessionMessageQueue sessionId="s1" />)
    expect(await screen.findByText('Sent, delivery unconfirmed')).toBeInTheDocument()
    expect(screen.getByText(/attempts 1/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Send now')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Remove')).not.toBeInTheDocument()
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Abandon message' }))
    expect(await screen.findByText(/may have been delivered/)).toBeInTheDocument()
    expect(screen.getByLabelText('Reason')).toBeInTheDocument()
    expect(screen.getByLabelText('Operator token')).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: 'Confirm abandonment' })).toBeDisabled()
    let abandoned = false
    server.use(
      http.post('/api/sessions/s1/messages/m1/abandon', async ({ request }) => {
        expect(request.headers.get('X-Antiphon-Operator-Token')).toBe('fixture-operator')
        expect(await request.json()).toEqual({ reason: 'Give up waiting for this input' })
        abandoned = true
        return HttpResponse.json({ sessionId: 's1', messageId: 'm1', status: 'Canceled',
          canceledAt: '2026-11-14T12:00:00Z', warning: 'It may have been delivered.' })
      }),
      http.get('/api/sessions/s1/messages', () => HttpResponse.json({ sessionId: 's1', messages: [], working: false })),
    )
    await user.type(screen.getByLabelText('Reason'), 'Give up waiting for this input')
    await user.type(screen.getByLabelText('Operator token'), 'fixture-operator')
    await user.click(screen.getByRole('button', { name: 'Confirm abandonment' }))
    await waitFor(() => expect(abandoned).toBe(true))
    await waitFor(() => expect(screen.queryByText('Sent, delivery unconfirmed')).not.toBeInTheDocument())
  })
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

  it('C514 renders modal blocked without parking or changing attempts', async () => {
    serve(
      [message({ deliveryAttempts: 0, parked: false, modalBlocked: true, body: 'held work prompt body' })],
      { modalBlocked: true, modalBlockedReason: 'Remote Control menu blocks input' },
    )

    renderWithProviders(<SessionMessageQueue sessionId="s1" />)

    expect(await screen.findByText('Remote Control menu blocks input')).toBeInTheDocument()
    expect(screen.getByText('Modal blocked')).toBeInTheDocument()
    expect(screen.getByText('attempts 0')).toBeInTheDocument()
    expect(screen.queryByText('Parked')).not.toBeInTheDocument()
    expect(screen.queryByText(/Delivered/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Recovered/i)).not.toBeInTheDocument()
  })
})
