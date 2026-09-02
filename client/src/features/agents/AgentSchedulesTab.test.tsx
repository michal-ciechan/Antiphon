import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSchedulesTab } from './AgentSchedulesTab'
import type { ScheduleDto } from '../../api/schedules'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

function schedule(overrides: Partial<ScheduleDto> = {}): ScheduleDto {
  return {
    id: 's1',
    name: 'Morning triage',
    kind: 'Prompt',
    repeat: 'Daily',
    repeatDescription: 'daily 09:00 Europe/London',
    timeZoneId: 'Europe/London',
    nextFireAt: '2026-09-03T08:00:00Z',
    nextFireAtLocal: '2026-09-03T09:00:00+01:00',
    enabled: true,
    missedGraceMinutes: 60,
    fireCount: 0,
    lastFiredAt: null,
    lastOutcome: null,
    lastOutcomeDetail: null,
    createdBy: null,
    createdAt: '2026-09-02T12:00:00Z',
    updatedAt: '2026-09-02T12:00:00Z',
    concurrencyToken: 'tok-1',
    agentId: 'a1',
    agentName: 'Family',
    agentSlug: 'family',
    promptText: 'triage',
    whenTargetDown: 'Queue',
    cardId: null,
    cardIdentifier: null,
    targetStatus: null,
    start: 'None',
    spendAcceptedAt: null,
    spendAcceptedBy: null,
    fireAt: null,
    everyMinutes: null,
    anchorAt: null,
    atLocal: '09:00',
    daysOfWeek: 0,
    ...overrides,
  }
}

describe('AgentSchedulesTab', () => {
  it('renders the schedule list', async () => {
    server.use(
      http.get('/api/schedules', () => HttpResponse.json({ schedules: [schedule()] })),
    )

    renderWithProviders(<AgentSchedulesTab agentId="a1" agentName="Family" />)

    expect(await screen.findByText('Morning triage')).toBeInTheDocument()
    expect(screen.getByText(/daily 09:00 Europe\/London/)).toBeInTheDocument()
  })

  it('toggles a schedule', async () => {
    const user = userEvent.setup()
    let enabled = true
    server.use(
      http.get('/api/schedules', () =>
        HttpResponse.json({ schedules: [schedule({ enabled, concurrencyToken: enabled ? 'tok-1' : 'tok-2' })] }),
      ),
      http.patch('/api/schedules/:id', async ({ request }) => {
        const body = (await request.json()) as { enabled: boolean }
        enabled = body.enabled
        return HttpResponse.json(schedule({ enabled, concurrencyToken: 'tok-2' }))
      }),
    )

    renderWithProviders(<AgentSchedulesTab agentId="a1" />)
    const toggle = await screen.findByRole('switch', { name: 'Toggle Morning triage' })
    expect(toggle).toBeChecked()
    await user.click(toggle)
    await waitFor(() => expect(enabled).toBe(false))
  })

  it('asks before deleting', async () => {
    const user = userEvent.setup()
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    let deleted = false
    server.use(
      http.get('/api/schedules', () => HttpResponse.json({ schedules: [schedule()] })),
      http.delete('/api/schedules/:id', () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderWithProviders(<AgentSchedulesTab agentId="a1" />)
    await user.click(await screen.findByRole('button', { name: 'Delete Morning triage' }))
    expect(confirm).toHaveBeenCalled()
    expect(deleted).toBe(false)

    confirm.mockReturnValue(true)
    await user.click(screen.getByRole('button', { name: 'Delete Morning triage' }))
    await waitFor(() => expect(deleted).toBe(true))
    confirm.mockRestore()
  })
})
