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
    // The refresh flips `enabled` too, so the dirty guard is asserted on every seeded field and not
    // only on the text the operator happened to touch.
    remote = { ...initial, concurrencyToken: 'revision-two', enabled: false, candidates: [...initial.candidates, { agentKind: 'ClaudeCode', modelLevel: 'High' }] }
    await act(async () => { await queryClient.invalidateQueries({ queryKey: agentKeys.detail('owner') }) })
    await waitFor(() => expect(queryClient.getQueryData<SpecialistRouting>(specialistRoutingKey('owner'))?.concurrencyToken).toBe('revision-two'))
    expect(input).toHaveValue('ClaudeCode/Low, Codex/Low')
    expect(screen.getByRole('switch', { name: 'Enable declared routing' })).toBeChecked()
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    await screen.findByText('Specialist routing changed; reload it before saving.')
    expect(writes).toEqual([{ concurrencyToken: 'revision-one', enabled: true,
      candidates: [{ agentKind: 'ClaudeCode', modelLevel: 'Low' }, { agentKind: 'Codex', modelLevel: 'Low' }] }])
    expect(screen.getByRole('button', { name: 'Revalidate' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Reload routing' }))
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low, ClaudeCode/High'))
    expect(screen.getByRole('switch', { name: 'Enable declared routing' })).not.toBeChecked()
  })

  it('adopts a clean refresh including enabled state and revision', async () => {
    let remote = initial
    const writes: unknown[] = []
    server.use(
      http.get(route, () => HttpResponse.json(remote)),
      http.put(route, async ({ request }) => { writes.push(await request.json()); return HttpResponse.json(remote) }),
    )
    const { queryClient } = renderWithProviders(<SpecialistRoutingPanel agentId="owner" />)
    const input = await screen.findByRole('textbox', { name: 'Ordered candidates' })
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low'))
    remote = { ...initial, concurrencyToken: 'revision-two', enabled: false, candidates: [{ agentKind: 'Codex', modelLevel: 'High' }] }
    await act(async () => { await queryClient.invalidateQueries({ queryKey: agentKeys.detail('owner') }) })
    await waitFor(() => expect(input).toHaveValue('Codex/High'))
    expect(screen.getByRole('switch', { name: 'Enable declared routing' })).not.toBeChecked()
    // The visible values and the concurrency token must move together: saving now has to carry
    // revision-two, or the form is showing one revision and writing against another.
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    await waitFor(() => expect(writes).toEqual([{ concurrencyToken: 'revision-two', enabled: false,
      candidates: [{ agentKind: 'Codex', modelLevel: 'High' }] }]))
  })

  it('reloads unchanged cached data after discarding edits', async () => {
    const writes: unknown[] = []
    server.use(
      http.get(route, () => HttpResponse.json(initial)),
      http.put(route, async ({ request }) => { writes.push(await request.json()); return HttpResponse.json(initial) }),
    )
    const { queryClient } = renderWithProviders(<SpecialistRoutingPanel agentId="owner" />)
    const input = await screen.findByRole('textbox', { name: 'Ordered candidates' })
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low'))
    const cached = queryClient.getQueryData<SpecialistRouting>(specialistRoutingKey('owner'))
    fireEvent.change(input, { target: { value: 'Codex/Low, Codex/High' } })
    await userEvent.click(screen.getByRole('switch', { name: 'Enable declared routing' }))
    expect(screen.getByRole('switch', { name: 'Enable declared routing' })).not.toBeChecked()
    await userEvent.click(screen.getByRole('button', { name: 'Reload routing' }))
    // The remote copy is byte-identical, so react-query's structural sharing hands back the SAME
    // object. Only the dirty-to-clean transition can re-seed the form here.
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low'))
    expect(queryClient.getQueryData<SpecialistRouting>(specialistRoutingKey('owner'))).toBe(cached)
    expect(screen.getByRole('switch', { name: 'Enable declared routing' })).toBeChecked()
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    await waitFor(() => expect(writes).toEqual([{ concurrencyToken: 'revision-one', enabled: true,
      candidates: [{ agentKind: 'ClaudeCode', modelLevel: 'Low' }] }]))
  })

  it('adopts the successful save response before the next save', async () => {
    const saved: SpecialistRouting = { ...initial, concurrencyToken: 'revision-two', enabled: false,
      candidates: [{ agentKind: 'Codex', modelLevel: 'Low' }, { agentKind: 'Codex', modelLevel: 'High' }] }
    let remote = initial
    const writes: unknown[] = []
    server.use(
      http.get(route, () => HttpResponse.json(remote)),
      http.put(route, async ({ request }) => { writes.push(await request.json()); remote = saved; return HttpResponse.json(saved) }),
    )
    renderWithProviders(<SpecialistRoutingPanel agentId="owner" />)
    const input = await screen.findByRole('textbox', { name: 'Ordered candidates' })
    await waitFor(() => expect(input).toHaveValue('ClaudeCode/Low'))
    fireEvent.change(input, { target: { value: 'Codex/Low, Codex/High' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    // The server's canonical answer wins once the form is clean again, token included.
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Enable declared routing' })).not.toBeChecked())
    expect(input).toHaveValue('Codex/Low, Codex/High')
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    await waitFor(() => expect(writes).toHaveLength(2))
    expect(writes[1]).toEqual({ concurrencyToken: 'revision-two', enabled: false,
      candidates: [{ agentKind: 'Codex', modelLevel: 'Low' }, { agentKind: 'Codex', modelLevel: 'High' }] })
  })

  it('seeds the primary pair and enabled default from cached data', async () => {
    const cached: SpecialistRouting = { ...initial, concurrencyToken: null, enabled: null, candidates: [] }
    const writes: unknown[] = []
    server.use(
      http.get(route, () => HttpResponse.json(cached)),
      http.put(route, async ({ request }) => { writes.push(await request.json()); return HttpResponse.json(cached) }),
    )
    // Data already in the cache on the panel's FIRST render — an effect would have painted the
    // empty form once before catching up.
    const { queryClient, rerender } = renderWithProviders(<></>)
    await act(async () => { queryClient.setQueryData(specialistRoutingKey('owner'), cached) })
    rerender(<SpecialistRoutingPanel agentId="owner" />)
    const input = screen.getByRole('textbox', { name: 'Ordered candidates' })
    expect(input).toHaveValue('ClaudeCode/Low')
    expect(screen.getByRole('switch', { name: 'Enable declared routing' })).toBeChecked()
    expect(screen.getByRole('button', { name: 'Revalidate' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Save routing' }))
    await waitFor(() => expect(writes).toEqual([{ concurrencyToken: null, enabled: true,
      candidates: [{ agentKind: 'ClaudeCode', modelLevel: 'Low' }] }]))
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
