import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import {
  complexityChainKeys,
  useClearComplexityChain,
  useComplexityChainEffective,
  useComplexityChains,
  usePutComplexityChain,
  type ComplexityChainDto,
  type ComplexityChainListDto,
} from './complexityChains'

const emptyList: ComplexityChainListDto = {
  chains: [],
  roles: ['Plan', 'Code'],
  complexities: ['Hard', 'Medium', 'Easy'],
}

const planEffective: ComplexityChainListDto = {
  roles: ['Plan', 'Code'],
  complexities: ['Hard', 'Medium', 'Easy'],
  chains: [
    {
      complexity: 'Hard',
      role: 'Plan',
      resolvedFrom: 'role',
      candidates: [],
      provenance: null,
      source: 'config',
      reason: null,
      notAfter: null,
      updatedAt: null,
    },
  ],
}

const savedRow: ComplexityChainDto = {
  complexity: 'Hard',
  role: 'Plan',
  resolvedFrom: 'role',
  candidates: [
    {
      agentKind: 'Codex',
      modelLevel: 'Frontier',
      alias: 'gpt-5.6-sol',
      availableNow: true,
      unavailableReason: null,
    },
  ],
  provenance: 'Human',
  source: 'pin',
  reason: 'plan-grade',
  notAfter: '2026-09-10T00:00:00Z',
  updatedAt: '2026-09-04T00:00:00Z',
}

describe('complexityChainKeys', () => {
  it('keeps list and per-role effective keys distinct', () => {
    expect(complexityChainKeys.list()).toEqual(['complexity-chains', 'list'])
    expect(complexityChainKeys.effective('Plan')).toEqual(['complexity-chains', 'effective', 'Plan'])
    expect(complexityChainKeys.list()).not.toEqual(complexityChainKeys.effective('Plan'))
  })
})

describe('useComplexityChains', () => {
  it('GETs /complexity-chains with no role query', async () => {
    const urls: string[] = []
    server.use(
      http.get('/api/complexity-chains', ({ request }) => {
        urls.push(new URL(request.url).pathname + new URL(request.url).search)
        return HttpResponse.json(emptyList)
      }),
    )

    const { result } = renderHookWithProviders(() => useComplexityChains())
    await waitFor(() => expect(result.current.data).toEqual(emptyList))
    expect(urls).toEqual(['/api/complexity-chains'])
  })
})

describe('useComplexityChainEffective', () => {
  it('GETs /complexity-chains?role=Plan for the effective cells', async () => {
    const urls: string[] = []
    server.use(
      http.get('/api/complexity-chains', ({ request }) => {
        const url = new URL(request.url)
        urls.push(url.pathname + url.search)
        expect(url.searchParams.get('role')).toBe('Plan')
        return HttpResponse.json(planEffective)
      }),
    )

    const { result } = renderHookWithProviders(() => useComplexityChainEffective('Plan'))
    await waitFor(() => expect(result.current.data).toEqual(planEffective))
    expect(urls).toEqual(['/api/complexity-chains?role=Plan'])
  })
})

describe('usePutComplexityChain', () => {
  it('PUTs a Human body to /complexity-chains/Plan/Hard and invalidates list and effective', async () => {
    const putSpy = vi.fn()
    server.use(
      http.put('/api/complexity-chains/Plan/Hard', async ({ request }) => {
        putSpy(new URL(request.url).pathname, await request.json())
        return HttpResponse.json(savedRow)
      }),
    )

    const { result, queryClient } = renderHookWithProviders(() => usePutComplexityChain())
    queryClient.setQueryData(complexityChainKeys.list(), emptyList)
    queryClient.setQueryData(complexityChainKeys.effective('Plan'), planEffective)
    queryClient.setQueryData(complexityChainKeys.effective('Code'), emptyList)

    result.current.mutate({
      role: 'Plan',
      complexity: 'Hard',
      candidates: [{ agentKind: 'Codex', modelLevel: 'Frontier' }],
      reason: 'plan-grade',
      notAfter: '2026-09-10T00:00:00Z',
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(putSpy).toHaveBeenCalledWith('/api/complexity-chains/Plan/Hard', {
      candidates: [{ agentKind: 'Codex', modelLevel: 'Frontier' }],
      provenance: 'Human',
      reason: 'plan-grade',
      notAfter: '2026-09-10T00:00:00Z',
    })
    expect(queryClient.getQueryState(complexityChainKeys.list())?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(complexityChainKeys.effective('Plan'))?.isInvalidated).toBe(
      true,
    )
    expect(queryClient.getQueryState(complexityChainKeys.effective('Code'))?.isInvalidated).toBe(
      true,
    )
  })

  it('PUTs the any-role row to /complexity-chains/any/Hard', async () => {
    const putSpy = vi.fn()
    server.use(
      http.put('/api/complexity-chains/any/Hard', async ({ request }) => {
        putSpy(new URL(request.url).pathname, await request.json())
        return HttpResponse.json({ ...savedRow, role: null, resolvedFrom: 'any' })
      }),
    )

    const { result } = renderHookWithProviders(() => usePutComplexityChain())
    result.current.mutate({
      role: null,
      complexity: 'Hard',
      candidates: [{ agentKind: 'Grok', modelLevel: 'Frontier' }],
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(putSpy).toHaveBeenCalledWith('/api/complexity-chains/any/Hard', {
      candidates: [{ agentKind: 'Grok', modelLevel: 'Frontier' }],
      provenance: 'Human',
      reason: null,
      notAfter: null,
    })
  })
})

describe('useClearComplexityChain', () => {
  it('DELETEs /complexity-chains/Plan/Hard and invalidates list and effective', async () => {
    const deleteSpy = vi.fn()
    server.use(
      http.delete('/api/complexity-chains/Plan/Hard', ({ request }) => {
        deleteSpy(new URL(request.url).pathname)
        return new HttpResponse(null, { status: 204 })
      }),
    )

    const { result, queryClient } = renderHookWithProviders(() => useClearComplexityChain())
    queryClient.setQueryData(complexityChainKeys.list(), emptyList)
    queryClient.setQueryData(complexityChainKeys.effective('Plan'), planEffective)

    result.current.mutate({ role: 'Plan', complexity: 'Hard' })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(deleteSpy).toHaveBeenCalledWith('/api/complexity-chains/Plan/Hard')
    expect(queryClient.getQueryState(complexityChainKeys.list())?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(complexityChainKeys.effective('Plan'))?.isInvalidated).toBe(
      true,
    )
  })

  it('DELETEs the any-role row at /complexity-chains/any/Hard', async () => {
    const deleteSpy = vi.fn()
    server.use(
      http.delete('/api/complexity-chains/any/Hard', ({ request }) => {
        deleteSpy(new URL(request.url).pathname)
        return new HttpResponse(null, { status: 204 })
      }),
    )

    const { result } = renderHookWithProviders(() => useClearComplexityChain())
    result.current.mutate({ role: null, complexity: 'Hard' })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(deleteSpy).toHaveBeenCalledWith('/api/complexity-chains/any/Hard')
  })
})
