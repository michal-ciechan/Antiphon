import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import type { ModelAvailabilityDto } from '../../api/modelAvailability'
import { ModelAvailabilityPanel } from './ModelAvailabilityPanel'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

const empty: ModelAvailabilityDto = {
  holds: [],
  available: ['fable', 'opus', 'sonnet', 'haiku', 'grok-4.6', 'gpt-5.6-sol', 'gpt-5.6-terra', 'gpt-5.6-luna'],
}

describe('ModelAvailabilityPanel', () => {
  it('shows the empty line when nothing is held', async () => {
    server.use(http.get('/api/model-availability', () => HttpResponse.json(empty)))
    renderWithProviders(<ModelAvailabilityPanel />)
    expect(await screen.findByText('All models available.')).toBeInTheDocument()
    expect(await screen.findByText(/available: fable, opus/)).toBeInTheDocument()
  })

  it('Hold PUTs kind, alias and until; Clear DELETEs that row', async () => {
    let snapshot: ModelAvailabilityDto = {
      holds: [
        {
          id: 'hold-1',
          kind: 'ClaudeCode',
          modelAlias: 'fable',
          source: 'Manual',
          disabledUntil: '2026-09-04T00:00:00Z',
          hitAt: '2026-09-01T12:00:00Z',
          reason: 'weekly cap',
          rawText: null,
          sourceSessionId: null,
          sourceTaskId: null,
        },
      ],
      available: ['opus', 'sonnet', 'haiku', 'grok-4.6'],
    }
    const puts: Array<{ url: string; body: unknown }> = []
    const deletes: string[] = []

    server.use(
      http.get('/api/model-availability', () => HttpResponse.json(snapshot)),
      http.put('/api/model-availability/:kind/:alias', async ({ request, params }) => {
        const body = await request.json()
        puts.push({ url: request.url, body })
        const hold = {
          id: 'hold-2',
          kind: String(params.kind),
          modelAlias: String(params.alias),
          source: 'Manual' as const,
          disabledUntil: (body as { disabledUntil?: string }).disabledUntil ?? null,
          hitAt: '2026-09-01T12:00:00Z',
          reason: (body as { reason?: string }).reason ?? 'manual hold',
          rawText: null,
          sourceSessionId: null,
          sourceTaskId: null,
        }
        snapshot = { ...snapshot, holds: [...snapshot.holds, hold] }
        return HttpResponse.json(hold)
      }),
      http.delete('/api/model-availability/:kind/:alias', ({ params }) => {
        deletes.push(`${params.kind}/${params.alias}`)
        snapshot = {
          ...snapshot,
          holds: snapshot.holds.filter(
            (h) => !(h.kind === params.kind && h.modelAlias === params.alias),
          ),
        }
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderWithProviders(<ModelAvailabilityPanel />)
    expect(await screen.findByText('weekly cap')).toBeInTheDocument()
    expect(screen.getByText('Manual')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Hold' }))
    await waitFor(() => expect(puts.length).toBe(1))
    expect(puts[0].url).toContain('/api/model-availability/ClaudeCode/fable')

    await userEvent.click(screen.getAllByRole('button', { name: 'Clear' })[0])
    await waitFor(() => expect(deletes).toEqual(['ClaudeCode/fable']))
  })
})
