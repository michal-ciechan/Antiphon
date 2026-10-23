import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import userEvent from '@testing-library/user-event'
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
    const unknown: AttentionItemDto = { ...item, kind: 'Overdue', taskId: 'unknown', title: 'Unknown observation',
      headline: 'Current provider dialog observation unavailable; the ordinary deadline still applies.' }
    const failed: AttentionItemDto = { ...item, kind: 'RecentFailure', taskId: 'failed', title: 'Failed delegate',
      headline: 'The task is Failed. The session was not killed; its runner seat may still be occupied.',
      evidence: 'New Refine calls on this task are refused. Preserve the worktree and pending queue.' }
    server.use(http.get('/api/attention', () => HttpResponse.json({ items: [item, unknown, failed], generatedAt: item.sinceUtc, runnerConsulted: false })))
    renderWithProviders(<AttentionPanel />)
    const row = await screen.findByTestId('attention-row-ProviderInputWait')
    expect(within(row).getByText('Provider input')).toBeVisible()
    expect(within(row).getByRole('button', { name: 'Read it first' })).toBeVisible()
    expect(within(row).queryByRole('button', { name: /approve|cancel|send now|kill|restart|continue/i })).not.toBeInTheDocument()
    const unknownRow = await screen.findByTestId('attention-row-Overdue')
    expect(within(unknownRow).getByText(unknown.headline)).toBeVisible()
    expect(within(unknownRow).queryByText('Provider input')).not.toBeInTheDocument()
    await userEvent.click(screen.getByText('Recent failures'))
    const failedRow = await screen.findByTestId('attention-row-RecentFailure')
    expect(within(failedRow).getByText(failed.evidence!)).toBeVisible()
    expect(within(failedRow).queryByRole('button', { name: /approve|cancel|send now|kill|restart|continue/i })).not.toBeInTheDocument()
  })
})
