import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { agentKeys } from '../../api/agents'
import { specialistRoutingKey, type SpecialistRouting } from '../../api/specialistRouting'
import { server } from '../../test/mocks/server'
import { act, fireEvent } from '@testing-library/react'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { SpecialistRoutingPanel } from './SpecialistRoutingPanel'

const route = '/api/agents/owner/specialist-routing'
const initial: SpecialistRouting = {
  agentId: 'owner', concurrencyToken: 'revision-one', enabled: true,
  primaryKind: 'ClaudeCode', primaryLevel: 'Low', primaryModelAlias: 'haiku',
  candidates: [{ agentKind: 'ClaudeCode', modelLevel: 'Low' }], candidateStates: [],
}

describe('Specialist routing', () => {
  it('retains the edited revision across AgentChanged refresh, rejects stale save and requires explicit reload', async () => {
    let remote = initial
    const writes: unknown[] = []
    server.use(
      http.get(route, () => HttpResponse.json(remote)),
      http.put(route, async ({ request }) => {
        writes.push(await request.json())
        return HttpResponse.json({ detail: 'Specialist routing changed; reload it before saving.' }, { status: 409 })
      }),
    )
    const { queryClient } = renderWithProviders(<SpecialistRoutingPanel agentId="owner" />)
    const input = await screen.findByRole('textbox', { name: 'Ordered candidates' })
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low'))
    fireEvent.change(input, { target: { value: 'ClaudeCode/Low, Codex/Low' } })
    remote = { ...initial, concurrencyToken: 'revision-two', candidates: [...initial.candidates, { agentKind: 'ClaudeCode', modelLevel: 'High' }] }
    await act(async () => { await queryClient.invalidateQueries({ queryKey: agentKeys.detail('owner') }) })
    await waitFor(() => expect(queryClient.getQueryData<SpecialistRouting>(specialistRoutingKey('owner'))?.concurrencyToken).toBe('revision-two'))
    expect(input).toHaveValue('ClaudeCode/Low, Codex/Low')
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    await screen.findByText('Specialist routing changed; reload it before saving.')
    expect(writes).toEqual([{ concurrencyToken: 'revision-one', enabled: true,
      candidates: [{ agentKind: 'ClaudeCode', modelLevel: 'Low' }, { agentKind: 'Codex', modelLevel: 'Low' }] }])
    expect(screen.getByRole('button', { name: 'Revalidate' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Reload routing' }))
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low, ClaudeCode/High'))
  })

  it('shows dependency and admission evidence and posts only the current revision to revalidate', async () => {
    const requests: unknown[] = []
    server.use(http.get(route, () => HttpResponse.json({ ...initial, candidateStates: [{
      id: 'codex', agentKind: 'Codex', modelLevel: 'Low', modelAlias: 'mini', physicalAgentId: null,
      enabled: true, status: 'PendingDependency', reason: 'Pending CARD-0167', declaredAt: '2026-09-09',
      unprovisionedAt: '2026-09-09', lastAdmissionRefusedAt: null, nextEligibleAt: null, transientFailures: 2,
    }] })), http.post(route + '/revalidate', async ({ request }) => {
      requests.push(await request.json())
      return HttpResponse.json({ ...initial, concurrencyToken: 'revision-two' })
    }))
    renderWithProviders(<SpecialistRoutingPanel agentId="owner" />)
    await screen.findByText('Pending CARD-0167')
    expect(screen.getByText(/next eligibility unknown/)).toBeInTheDocument()
    expect(screen.getByText(/transient failures 2/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Revalidate' }))
    await waitFor(() => expect(requests).toEqual([{ concurrencyToken: 'revision-one' }]))
  })
})
