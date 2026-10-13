import { HttpResponse, http } from 'msw'
import { expect, it } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import {
  dispatchConcurrencyKeys,
  isProjectScope,
  useDispatchConcurrency,
  useDispatchConcurrencyRevisions,
  usePutDispatchConcurrency,
  type DispatchConcurrencyEffectiveDto,
  type DispatchConcurrencyGlobalDto,
  type DispatchConcurrencyOccupancyDto,
  type DispatchConcurrencyProjectDto,
  type DispatchConcurrencyRevisionPageDto,
} from './dispatchConcurrency'

// CARD-0506 test design F-1/F-2 fixtures, built locally: G7 (global revision 7), P0 (project
// without a row), P4 (project revision 4) and one history row r3.
const P = '05060000-0000-4000-8000-000000000001'
const ROLES = [
  'Custom', 'Plan', 'Code', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge',
  'Investigate', 'TestDesign', 'Mutation',
]
const RANGES = { parallelMin: 1, parallelMax: 512, queuedMin: 0, queuedMax: 4096 }
const SEED = {
  origin: 'startupConfiguration',
  importedAt: '2026-10-13T10:00:00Z',
  mode: 'LegacyOpen',
  maxParallel: 6,
  maxQueued: null,
  roles: ROLES.map((role) => ({ role, maxParallel: 2, maxQueued: null })),
}
const G7_OVERRIDES = { schemaVersion: 1, maxParallel: 4, maxQueued: 12, roles: { Code: { maxParallel: 3 }, Review: { maxQueued: 5 } } }

function g7Effective(globalRevision: number, projectRevision: number): DispatchConcurrencyEffectiveDto {
  return {
    mode: 'LegacyOpen',
    modeSource: 'default',
    maxParallel: 4,
    maxParallelSource: 'global',
    maxQueued: 12,
    maxQueuedSource: 'global',
    roles: ROLES.map((role) =>
      role === 'Code'
        ? { role, maxParallel: 3, maxParallelSource: 'global', maxQueued: null, maxQueuedSource: 'default', combinedParallel: 3 }
        : role === 'Review'
          ? { role, maxParallel: 2, maxParallelSource: 'default', maxQueued: 5, maxQueuedSource: 'global', combinedParallel: 2 }
          : { role, maxParallel: 2, maxParallelSource: 'default', maxQueued: null, maxQueuedSource: 'default', combinedParallel: 2 },
    ),
    globalRevision,
    projectRevision,
  }
}

function occupancyFor(effective: DispatchConcurrencyEffectiveDto): DispatchConcurrencyOccupancyDto {
  return {
    open: 0,
    parallel: 0,
    queued: 0,
    parallelRemaining: effective.maxParallel,
    queuedRemaining: effective.maxQueued,
    parallelOverage: 0,
    queuedOverage: 0,
    exceedsBounds: false,
    roles: effective.roles.map((role) => ({
      role: role.role,
      open: 0,
      parallel: 0,
      queued: 0,
      parallelRemaining: role.combinedParallel,
      queuedRemaining: role.maxQueued,
      parallelOverage: 0,
      queuedOverage: 0,
    })),
  }
}

const G7: DispatchConcurrencyGlobalDto = {
  seed: SEED,
  overrides: G7_OVERRIDES,
  effective: g7Effective(7, 0),
  nullProject: g7Effective(7, 0),
  supportedRoles: ROLES,
  ranges: RANGES,
  revision: 7,
  updatedAt: '2026-10-13T10:05:00Z',
  reason: 'operator',
  provenance: 'Human',
  callerTaskId: null,
  occupancy: occupancyFor(g7Effective(7, 0)),
}

const P0: DispatchConcurrencyProjectDto = {
  projectId: P,
  overrides: { schemaVersion: 1 },
  inherited: g7Effective(7, 0),
  effective: g7Effective(7, 0),
  supportedRoles: ROLES,
  ranges: RANGES,
  revision: 0,
  globalRevision: 7,
  updatedAt: null,
  reason: null,
  provenance: null,
  callerTaskId: null,
  occupancy: occupancyFor(g7Effective(7, 0)),
}

function p4Effective(): DispatchConcurrencyEffectiveDto {
  const base = g7Effective(7, 4)
  return {
    ...base,
    roles: base.roles.map((role) =>
      role.role === 'Code'
        ? { ...role, maxParallel: 1, maxParallelSource: 'project', combinedParallel: 1 }
        : role.role === 'Review'
          ? { ...role, maxQueued: null, maxQueuedSource: 'project' }
          : role,
    ),
  }
}

const P4: DispatchConcurrencyProjectDto = {
  projectId: P,
  overrides: { schemaVersion: 1, roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } },
  inherited: g7Effective(7, 4),
  effective: p4Effective(),
  supportedRoles: ROLES,
  ranges: RANGES,
  revision: 4,
  globalRevision: 7,
  updatedAt: '2026-10-13T10:10:00Z',
  reason: 'project operator',
  provenance: 'Human',
  callerTaskId: null,
  occupancy: occupancyFor(p4Effective()),
}

function historyR3(scopeKey: string, projectId: string | null): DispatchConcurrencyRevisionPageDto {
  return {
    revisions: [
      {
        revision: 3,
        previousRevision: 2,
        snapshot: { scopeKey, projectId, revision: 3, provenance: 'Human', reason: 'earlier', callerTaskId: null, overrides: { schemaVersion: 1 } },
        createdAt: '2026-10-12T09:00:00Z',
        reason: 'earlier',
        provenance: 'Human',
        callerTaskId: null,
      },
    ],
    nextBeforeRevision: null,
  }
}

const FIVE_KEYS = ['expectedGlobalRevision', 'expectedRevision', 'overrides', 'provenance', 'reason']

// ---- V-5 PUT body: exactly five fields from the loaded revisions, Human, trimmed reason ----

it('C506 V05 global', async () => {
  let raw = ''
  let parsed: Record<string, unknown> | null = null
  server.use(
    http.get('/api/dispatch-concurrency', () => HttpResponse.json(G7)),
    http.put('/api/dispatch-concurrency', async ({ request }) => {
      raw = await request.text()
      parsed = JSON.parse(raw) as Record<string, unknown>
      return HttpResponse.json({ ...G7, revision: 8 })
    }),
  )
  const view = renderHookWithProviders(() => ({ scope: useDispatchConcurrency(null), save: usePutDispatchConcurrency(null) }))
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(7))
  const loaded = view.result.current.scope.data!
  expect(isProjectScope(loaded)).toBe(false)
  view.result.current.save.mutate({
    expectedRevision: loaded.revision,
    expectedGlobalRevision: loaded.revision,
    overrides: { maxParallel: 4, roles: { Review: { maxQueued: null } } },
    reason: '  operator  ',
  })
  await waitFor(() => expect(parsed).not.toBeNull())
  expect(parsed).toStrictEqual({
    expectedRevision: 7,
    expectedGlobalRevision: 7,
    overrides: { maxParallel: 4, roles: { Review: { maxQueued: null } } },
    reason: 'operator',
    provenance: 'Human',
  })
  expect(Object.keys(parsed!).sort()).toStrictEqual(FIVE_KEYS)
  expect(parsed!.provenance).toBe('Human')
  expect(raw).toContain('"maxQueued":null')
})

it('C506 V05 project-zero', async () => {
  const getIds: string[] = []
  const putIds: string[] = []
  let parsed: Record<string, unknown> | null = null
  server.use(
    http.get('/api/projects/:projectId/dispatch-concurrency', ({ params }) => {
      getIds.push(String(params.projectId))
      return HttpResponse.json(P0)
    }),
    http.put('/api/projects/:projectId/dispatch-concurrency', async ({ params, request }) => {
      putIds.push(String(params.projectId))
      parsed = (await request.json()) as Record<string, unknown>
      return HttpResponse.json({ ...P0, revision: 1 })
    }),
  )
  const view = renderHookWithProviders(() => ({ scope: useDispatchConcurrency(P), save: usePutDispatchConcurrency(P) }))
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(0))
  const loaded = view.result.current.scope.data!
  expect(isProjectScope(loaded)).toBe(true)
  const globalRevision = isProjectScope(loaded) ? loaded.globalRevision : -1
  view.result.current.save.mutate({
    expectedRevision: loaded.revision,
    expectedGlobalRevision: globalRevision,
    overrides: { roles: { Code: { maxParallel: 1 } } },
    reason: 'first override',
  })
  await waitFor(() => expect(parsed).not.toBeNull())
  // The success invalidation may already have refetched the scope; every GET must still name P.
  expect(getIds.length).toBeGreaterThanOrEqual(1)
  expect(getIds.every((id) => id === P)).toBe(true)
  expect(putIds).toStrictEqual([P])
  expect(parsed).toStrictEqual({
    expectedRevision: 0,
    expectedGlobalRevision: 7,
    overrides: { roles: { Code: { maxParallel: 1 } } },
    reason: 'first override',
    provenance: 'Human',
  })
  expect(parsed!.expectedRevision).toBe(0)
})

it('C506 V05 project-existing', async () => {
  const putIds: string[] = []
  let raw = ''
  let parsed: Record<string, unknown> | null = null
  server.use(
    http.get('/api/projects/:projectId/dispatch-concurrency', () => HttpResponse.json(P4)),
    http.put('/api/projects/:projectId/dispatch-concurrency', async ({ params, request }) => {
      putIds.push(String(params.projectId))
      raw = await request.text()
      parsed = JSON.parse(raw) as Record<string, unknown>
      return HttpResponse.json({ ...P4, revision: 5 })
    }),
  )
  const view = renderHookWithProviders(() => ({ scope: useDispatchConcurrency(P), save: usePutDispatchConcurrency(P) }))
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(4))
  const loaded = view.result.current.scope.data!
  const globalRevision = isProjectScope(loaded) ? loaded.globalRevision : -1
  view.result.current.save.mutate({
    expectedRevision: loaded.revision,
    expectedGlobalRevision: globalRevision,
    overrides: { roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } },
    reason: 'keep',
  })
  await waitFor(() => expect(parsed).not.toBeNull())
  expect(putIds).toStrictEqual([P])
  expect(parsed).toStrictEqual({
    expectedRevision: 4,
    expectedGlobalRevision: 7,
    overrides: { roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } },
    reason: 'keep',
    provenance: 'Human',
  })
  expect(Object.hasOwn(parsed!, 'expectedGlobalRevision')).toBe(true)
  expect(parsed!.expectedGlobalRevision).toBe(7)
  expect(raw).toContain('"maxQueued":null')
})

// ---- V-6 success refreshes the scope and its history, nothing else ----

it('C506 V06 global', async () => {
  let state: DispatchConcurrencyGlobalDto = G7
  let history = historyR3('global', null)
  let scopeGets = 0
  let historyGets = 0
  const limits: string[] = []
  server.use(
    http.get('/api/dispatch-concurrency', () => {
      scopeGets++
      return HttpResponse.json(state)
    }),
    http.get('/api/dispatch-concurrency/revisions', ({ request }) => {
      historyGets++
      limits.push(new URL(request.url).searchParams.get('limit') ?? '(none)')
      return HttpResponse.json(history)
    }),
    http.put('/api/dispatch-concurrency', async ({ request }) => {
      const body = (await request.json()) as { overrides: Record<string, unknown>; reason: string }
      state = { ...state, revision: 8, overrides: { schemaVersion: 1, ...body.overrides }, reason: body.reason }
      history = {
        revisions: [
          {
            revision: 8,
            previousRevision: 7,
            snapshot: { scopeKey: 'global', projectId: null, revision: 8, provenance: 'Human', reason: body.reason, callerTaskId: null, overrides: state.overrides },
            createdAt: '2026-10-13T10:20:00Z',
            reason: body.reason,
            provenance: 'Human',
            callerTaskId: null,
          },
          ...history.revisions,
        ],
        nextBeforeRevision: null,
      }
      return HttpResponse.json(state)
    }),
  )
  const view = renderHookWithProviders(() => ({
    scope: useDispatchConcurrency(null),
    history: useDispatchConcurrencyRevisions(null),
    save: usePutDispatchConcurrency(null),
  }))
  view.queryClient.setQueryData(['unrelated'], 'kept')
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(7))
  await waitFor(() => expect(view.result.current.history.data?.revisions[0]?.revision).toBe(3))
  expect(scopeGets).toBe(1)
  expect(historyGets).toBe(1)
  view.result.current.save.mutate({ expectedRevision: 7, expectedGlobalRevision: 7, overrides: { maxParallel: 5 }, reason: 'raise' })
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(8))
  await waitFor(() => expect(view.result.current.history.data?.revisions[0]?.revision).toBe(8))
  expect(scopeGets).toBe(2)
  expect(historyGets).toBe(2)
  expect(limits).toStrictEqual(['10', '10'])
  expect(view.queryClient.getQueryData(['unrelated'])).toBe('kept')
  expect(view.queryClient.getQueryState(['unrelated'])?.isInvalidated).toBe(false)
})

it('C506 V06 project', async () => {
  let state: DispatchConcurrencyProjectDto = P4
  let history = historyR3(P, P)
  let scopeGets = 0
  let historyGets = 0
  const limits: string[] = []
  const ids: string[] = []
  server.use(
    http.get('/api/projects/:projectId/dispatch-concurrency', ({ params }) => {
      scopeGets++
      ids.push(String(params.projectId))
      return HttpResponse.json(state)
    }),
    http.get('/api/projects/:projectId/dispatch-concurrency/revisions', ({ params, request }) => {
      historyGets++
      ids.push(String(params.projectId))
      limits.push(new URL(request.url).searchParams.get('limit') ?? '(none)')
      return HttpResponse.json(history)
    }),
    http.put('/api/projects/:projectId/dispatch-concurrency', async ({ params, request }) => {
      ids.push(String(params.projectId))
      const body = (await request.json()) as { overrides: Record<string, unknown>; reason: string }
      state = { ...state, revision: 5, overrides: { schemaVersion: 1, ...body.overrides }, reason: body.reason }
      history = {
        revisions: [
          {
            revision: 5,
            previousRevision: 4,
            snapshot: { scopeKey: P, projectId: P, revision: 5, provenance: 'Human', reason: body.reason, callerTaskId: null, overrides: state.overrides },
            createdAt: '2026-10-13T10:20:00Z',
            reason: body.reason,
            provenance: 'Human',
            callerTaskId: null,
          },
          ...history.revisions,
        ],
        nextBeforeRevision: null,
      }
      return HttpResponse.json(state)
    }),
  )
  const view = renderHookWithProviders(() => ({
    scope: useDispatchConcurrency(P),
    history: useDispatchConcurrencyRevisions(P),
    save: usePutDispatchConcurrency(P),
  }))
  view.queryClient.setQueryData(['unrelated'], 'kept')
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(4))
  await waitFor(() => expect(view.result.current.history.data?.revisions[0]?.revision).toBe(3))
  expect(scopeGets).toBe(1)
  expect(historyGets).toBe(1)
  view.result.current.save.mutate({
    expectedRevision: 4,
    expectedGlobalRevision: 7,
    overrides: { roles: { Code: { maxParallel: 2 } } },
    reason: 'loosen',
  })
  await waitFor(() => expect(view.result.current.scope.data?.revision).toBe(5))
  await waitFor(() => expect(view.result.current.history.data?.revisions[0]?.revision).toBe(5))
  expect(scopeGets).toBe(2)
  expect(historyGets).toBe(2)
  expect(limits).toStrictEqual(['10', '10'])
  expect(ids.every((id) => id === P)).toBe(true)
  expect(view.queryClient.getQueryData(['unrelated'])).toBe('kept')
  expect(view.queryClient.getQueryState(['unrelated'])?.isInvalidated).toBe(false)
})

// ---- F1 sweep (Review ef9c0e6b): server- and caller-provided strings are data ----

it('C506 F1 keys-project-named-global', () => {
  expect(dispatchConcurrencyKeys.scope(null)).toStrictEqual(['dispatchConcurrency', 'scope', null])
  expect(dispatchConcurrencyKeys.scope('global')).toStrictEqual(['dispatchConcurrency', 'scope', 'global'])
  expect(dispatchConcurrencyKeys.revisions(undefined)).toStrictEqual(['dispatchConcurrency', 'revisions', null])
  expect(dispatchConcurrencyKeys.revisions('global')).toStrictEqual(['dispatchConcurrency', 'revisions', 'global'])
})

it('C506 F1 project-scope-own-property', () => {
  const inherited = Object.create({ projectId: P }) as DispatchConcurrencyGlobalDto
  expect(isProjectScope(inherited)).toBe(false)
  expect(isProjectScope(P0)).toBe(true)
})

it('C506 F1 project-id-path-encoded', async () => {
  const paths: string[] = []
  server.use(
    http.get(/\/api\/projects\/.+\/dispatch-concurrency(\/revisions)?$/, ({ request }) => {
      const url = new URL(request.url)
      paths.push(url.pathname + url.search)
      return HttpResponse.json(url.pathname.endsWith('/revisions') ? historyR3(P, P) : P0)
    }),
  )
  const view = renderHookWithProviders(() => ({
    scope: useDispatchConcurrency('a/b?c'),
    history: useDispatchConcurrencyRevisions('a/b?c'),
  }))
  await waitFor(() => expect(view.result.current.scope.isSuccess && view.result.current.history.isSuccess).toBe(true))
  expect(paths.sort()).toStrictEqual([
    '/api/projects/a%2Fb%3Fc/dispatch-concurrency',
    '/api/projects/a%2Fb%3Fc/dispatch-concurrency/revisions?limit=10',
  ])
})
