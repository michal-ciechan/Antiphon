import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AttentionItemDto } from '../../api/attention'
import { renderWithProviders, screen, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AttentionPanel } from './AttentionPanel'
import { visualOf, targetOf } from './attentionVisuals'
import { LIVENESS_KINDS } from '../home/tasks/homeTasksModel'

describe('C1518 provider input decision', () => {
  it('C1518 maps the server kind and shows only navigation actions', async () => {
    expect(visualOf('ProviderInputWait').label).toBe('Provider input')
    expect(visualOf('ProviderInputWait').color).toBe('warning')
    expect(LIVENESS_KINDS.has('ProviderInputWait')).toBe(true)
    const item: AttentionItemDto = {
      kind: 'ProviderInputWait', taskId: 'task', sessionId: 'session', agentId: 'agent', messageId: null,
      severity: 'Warning', title: 'Permission wait', headline: 'Decision needed', evidence: '90-minute deadline remains',
      subtreeCostUsd: null, sinceUtc: '2026-10-23T00:00:00Z', actions: ['OpenDrawer', 'OpenAgent', 'OpenCard'],
    }
    expect(targetOf(item)).toBe('/orchestrator?tab=delegations&task=task')
    server.use(http.get('/api/attention', () => HttpResponse.json({ items: [item], generatedAt: item.sinceUtc, runnerConsulted: false })))
    renderWithProviders(<AttentionPanel />)
    const row = await screen.findByTestId('attention-row-ProviderInputWait')
    expect(within(row).getByText('Provider input')).toBeVisible()
    expect(within(row).getByRole('button', { name: 'Read it first' })).toBeVisible()
    expect(within(row).queryByRole('button', { name: /approve|cancel|send now|kill|restart|continue/i })).not.toBeInTheDocument()
  })
})
