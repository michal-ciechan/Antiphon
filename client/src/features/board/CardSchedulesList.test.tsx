import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { CardSchedulesList } from './CardSchedulesList'

describe('CardSchedulesList', () => {
  it('states the spend mode in words', async () => {
    server.use(
      http.get('/api/schedules', () =>
        HttpResponse.json({
          schedules: [
            {
              id: 'c1',
              name: 'Thursday kickoff',
              kind: 'Card',
              repeat: 'Daily',
              repeatDescription: 'daily 09:00 Europe/London, Thu',
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
              concurrencyToken: 'tok',
              agentId: null,
              agentName: null,
              agentSlug: null,
              promptText: null,
              whenTargetDown: 'Skip',
              cardId: 'card-1',
              cardIdentifier: 'CARD-0001',
              targetStatus: 'InProgress',
              start: 'Release',
              spendAcceptedAt: '2026-09-02T12:00:00Z',
              spendAcceptedBy: 'operator',
              fireAt: null,
              everyMinutes: null,
              anchorAt: null,
              atLocal: '09:00',
              daysOfWeek: 8,
            },
          ],
        }),
      ),
    )

    renderWithProviders(<CardSchedulesList cardId="card-1" />)

    expect(await screen.findByTestId('card-schedules-list')).toBeInTheDocument()
    expect(screen.getByText('Thursday kickoff')).toBeInTheDocument()
    expect(screen.getByTestId('card-schedule-spend-c1').textContent).toMatch(/will start a session/i)
    expect(screen.getByTestId('card-schedule-spend-c1').textContent).toMatch(/orchestrator/i)
  })
})
