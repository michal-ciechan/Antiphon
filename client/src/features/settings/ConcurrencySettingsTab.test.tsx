import { HttpResponse, http } from 'msw'
import { afterEach, expect, it, vi } from 'vitest'
import type {
  DispatchConcurrencyEffectiveDto,
  DispatchConcurrencyGlobalDto,
  DispatchConcurrencyOccupancyDto,
  DispatchConcurrencyProjectDto,
} from '../../api/dispatchConcurrency'
import type { ProjectDto } from '../../api/projects'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SettingsPage } from './SettingsPage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

// CARD-0506 test design V-15 (F-2 fixtures, built locally): G7 global, P4 project with overrides,
// Q0 project without a row, U a well-formed project id the server does not know (404).
const P = '05060000-0000-4000-8000-000000000001'
const Q = '05060000-0000-4000-8000-000000000002'
const U = '05060000-0000-4000-8000-0000000000ff'
const ROLES = [
  'Custom', 'Plan', 'Code', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge',
  'Investigate', 'TestDesign', 'Mutation',
]
const RANGES = { parallelMin: 1, parallelMax: 512, queuedMin: 0, queuedMax: 4096 }

type RoleRow = DispatchConcurrencyEffectiveDto['roles'][number]

function g7Effective(projectRevision: number): DispatchConcurrencyEffectiveDto {
  return {
    mode: 'LegacyOpen',
    modeSource: 'default',
    maxParallel: 4,
    maxParallelSource: 'global',
    maxQueued: 12,
    maxQueuedSource: 'global',
    roles: ROLES.map((role): RoleRow =>
      role === 'Code'
        ? { role, maxParallel: 3, maxParallelSource: 'global', maxQueued: null, maxQueuedSource: 'default', combinedParallel: 3 }
        : role === 'Review'
          ? { role, maxParallel: 2, maxParallelSource: 'default', maxQueued: 5, maxQueuedSource: 'global', combinedParallel: 2 }
          : { role, maxParallel: 2, maxParallelSource: 'default', maxQueued: null, maxQueuedSource: 'default', combinedParallel: 2 },
    ),
    globalRevision: 7,
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
    roles: effective.roles.map((row) => ({
      role: row.role,
      open: 0,
      parallel: 0,
      queued: 0,
      parallelRemaining: row.combinedParallel,
      queuedRemaining: row.maxQueued,
      parallelOverage: 0,
      queuedOverage: 0,
    })),
  }
}

const G7: DispatchConcurrencyGlobalDto = {
  seed: {
    origin: 'startupConfiguration',
    importedAt: '2026-10-13T10:00:00Z',
    mode: 'LegacyOpen',
    maxParallel: 6,
    maxQueued: null,
    roles: ROLES.map((role) => ({ role, maxParallel: 2, maxQueued: null })),
  },
  overrides: { schemaVersion: 1, maxParallel: 4, maxQueued: 12, roles: { Code: { maxParallel: 3 }, Review: { maxQueued: 5 } } },
  effective: g7Effective(0),
  nullProject: g7Effective(0),
  supportedRoles: ROLES,
  ranges: RANGES,
  revision: 7,
  updatedAt: '2026-10-13T10:05:00Z',
  reason: 'operator',
  provenance: 'Human',
  callerTaskId: null,
  occupancy: occupancyFor(g7Effective(0)),
}

function projectDto(
  projectId: string,
  revision: number,
  overrides: Record<string, unknown>,
  effective: DispatchConcurrencyEffectiveDto,
): DispatchConcurrencyProjectDto {
  return {
    projectId,
    overrides,
    inherited: g7Effective(revision),
    effective,
    supportedRoles: ROLES,
    ranges: RANGES,
    revision,
    globalRevision: 7,
    updatedAt: revision === 0 ? null : '2026-10-13T10:10:00Z',
    reason: revision === 0 ? null : 'project operator',
    provenance: revision === 0 ? null : 'Human',
    callerTaskId: null,
    occupancy: occupancyFor(effective),
  }
}

const P4 = projectDto(P, 4, { schemaVersion: 1, roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } }, {
  ...g7Effective(4),
  roles: g7Effective(4).roles.map((row) =>
    row.role === 'Code'
      ? { ...row, maxParallel: 1, maxParallelSource: 'project', combinedParallel: 1 }
      : row.role === 'Review'
        ? { ...row, maxQueued: null, maxQueuedSource: 'project' }
        : row,
  ),
})
const Q0 = projectDto(Q, 0, { schemaVersion: 1 }, g7Effective(0))
const SCOPES: Record<string, DispatchConcurrencyProjectDto> = { [P]: P4, [Q]: Q0 }

function projectRecord(id: string, name: string): ProjectDto {
  return {
    id,
    name,
    gitRepositoryUrl: `https://example.test/${name.toLowerCase()}.git`,
    localRepositoryPath: `/repos/${name.toLowerCase()}`,
    baseBranch: 'master',
    constitutionPath: 'AGENTS.md',
    gitHubIntegrationEnabled: false,
    notificationsEnabled: false,
    createdAt: '2026-08-20T12:00:00Z',
    updatedAt: '2026-08-20T12:00:00Z',
    defaultLaunchEnv: {},
  }
}

interface Calls {
  globalGets: number
  projectGets: string[]
  puts: { path: string; body: unknown }[]
}

/** F-3 handlers: global and per-project routes recorded separately; unknown projects are 404. */
function serveScopes(): Calls {
  const calls: Calls = { globalGets: 0, projectGets: [], puts: [] }
  const emptyHistory = { revisions: [], nextBeforeRevision: null }
  server.use(
    http.get('/api/projects', () => HttpResponse.json([projectRecord(P, 'P'), projectRecord(Q, 'Q')])),
    http.get('/api/dispatch-concurrency', () => {
      calls.globalGets += 1
      return HttpResponse.json(G7)
    }),
    http.get('/api/dispatch-concurrency/revisions', () => HttpResponse.json(emptyHistory)),
    http.get('/api/projects/:projectId/dispatch-concurrency', ({ params }) => {
      const id = String(params.projectId)
      calls.projectGets.push(id)
      return Object.hasOwn(SCOPES, id)
        ? HttpResponse.json(SCOPES[id])
        : HttpResponse.json({ title: 'Not Found', status: 404, detail: `Project ${id} was not found.` }, { status: 404 })
    }),
    http.get('/api/projects/:projectId/dispatch-concurrency/revisions', ({ params }) =>
      Object.hasOwn(SCOPES, String(params.projectId))
        ? HttpResponse.json(emptyHistory)
        : HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 }),
    ),
    http.put('/api/dispatch-concurrency', async ({ request }) => {
      calls.puts.push({ path: '/api/dispatch-concurrency', body: await request.json() })
      return HttpResponse.json(G7)
    }),
    http.put('/api/projects/:projectId/dispatch-concurrency', async ({ request, params }) => {
      const id = String(params.projectId)
      calls.puts.push({ path: `/api/projects/${id}/dispatch-concurrency`, body: await request.json() })
      return HttpResponse.json(SCOPES[id])
    }),
  )
  return calls
}

function search() {
  return new URLSearchParams(window.location.search)
}

async function chooseScope(label: string) {
  await userEvent.click(screen.getByRole('textbox', { name: 'Scope' }))
  await userEvent.click(await screen.findByRole('option', { name: label }))
}

afterEach(() => {
  window.history.pushState({}, '', '/')
})

it('C506 V15 project-deeplink', async () => {
  const calls = serveScopes()
  window.history.pushState({}, '', `/settings?tab=concurrency&project=${P}`)

  renderWithProviders(<SettingsPage />)

  expect(await screen.findByRole('tab', { name: 'Concurrency' })).toHaveAttribute('aria-selected', 'true')
  expect(await screen.findByTestId('dispatch-concurrency-subtitle')).toHaveTextContent('Revision 4 · Human · project operator')
  const section = screen.getByTestId('dispatch-concurrency-section')
  expect(within(section).getByText('Project concurrency')).toBeInTheDocument()
  expect(screen.getByRole('switch', { name: 'Code max parallel override' })).toBeChecked()
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Scope' })).toHaveValue('P'))
  expect(calls.projectGets).toEqual([P])
  expect(calls.globalGets).toBe(0)
})

it('C506 V15 scope-selector', async () => {
  const calls = serveScopes()
  window.history.pushState({}, '', `/settings?tab=concurrency&project=${P}`)

  renderWithProviders(<SettingsPage />)

  expect(await screen.findByTestId('dispatch-concurrency-subtitle')).toHaveTextContent('Revision 4')
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Scope' })).toHaveValue('P'))
  // A P draft: one more override and a reason, never saved.
  await userEvent.click(screen.getByRole('switch', { name: 'Review max parallel override' }))
  await userEvent.type(screen.getByRole('textbox', { name: /Reason/ }), 'p draft')

  await chooseScope('Q')

  await waitFor(() =>
    expect(screen.getByTestId('dispatch-concurrency-subtitle')).toHaveTextContent('Inheriting global defaults; no project overrides'),
  )
  expect(search().get('tab')).toBe('concurrency')
  expect(search().get('project')).toBe(Q)
  expect(calls.projectGets).toEqual([P, Q])
  expect(screen.getByRole('switch', { name: 'Code max parallel override' })).not.toBeChecked()
  expect(screen.getByRole('switch', { name: 'Review max parallel override' })).not.toBeChecked()
  expect(screen.getByRole('textbox', { name: /Reason/ })).toHaveValue('')

  await userEvent.type(screen.getByRole('textbox', { name: /Reason/ }), 'q reason')
  await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(calls.puts).toHaveLength(1))
  expect(calls.puts[0]).toEqual({
    path: `/api/projects/${Q}/dispatch-concurrency`,
    body: { expectedRevision: 0, expectedGlobalRevision: 7, overrides: {}, reason: 'q reason', provenance: 'Human' },
  })

  await chooseScope('Global defaults')

  await waitFor(() => expect(screen.getByTestId('dispatch-concurrency-subtitle')).toHaveTextContent('Revision 7 · Human · operator'))
  expect(within(screen.getByTestId('dispatch-concurrency-section')).getByText('Global concurrency defaults')).toBeInTheDocument()
  expect(search().get('tab')).toBe('concurrency')
  expect(search().has('project')).toBe(false)
  expect(calls.globalGets).toBe(1)
  expect(screen.getByRole('textbox', { name: /Reason/ })).toHaveValue('')
  expect(calls.puts).toHaveLength(1)
})

it('C506 V15 missing-project', async () => {
  const calls = serveScopes()
  window.history.pushState({}, '', `/settings?tab=concurrency&project=${U}`)

  renderWithProviders(<SettingsPage />)

  const failed = await screen.findByTestId('dispatch-concurrency-load-error')
  expect(within(screen.getByTestId('dispatch-concurrency-section')).getByText('Project concurrency')).toBeInTheDocument()
  expect(calls.projectGets).toEqual([U])
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Scope' })).toHaveValue(`Unknown project (${U})`))

  await userEvent.click(within(failed).getByRole('button', { name: 'Retry' }))
  await waitFor(() => expect(calls.projectGets).toEqual([U, U]))
  expect(await screen.findByTestId('dispatch-concurrency-load-error')).toBeInTheDocument()

  expect(screen.queryByTestId('dispatch-concurrency-save')).toBeNull()
  expect(screen.queryByTestId('dispatch-concurrency-table')).toBeNull()
  expect(calls.globalGets).toBe(0)
  expect(calls.puts).toHaveLength(0)
  expect(search().get('project')).toBe(U)
})
