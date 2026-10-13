import type { HubConnection } from '@microsoft/signalr'
import { notifications } from '@mantine/notifications'
import { HttpResponse, http } from 'msw'
import { beforeEach, expect, it, vi } from 'vitest'
import { act, renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { useSignalRInvalidation } from '../../hooks/useSignalRInvalidation'
import type {
  DispatchConcurrencyEffectiveDto,
  DispatchConcurrencyGlobalDto,
  DispatchConcurrencyOccupancyDto,
  DispatchConcurrencyProjectDto,
  DispatchConcurrencyRevisionPageDto,
  DispatchConcurrencyScopeDto,
} from '../../api/dispatchConcurrency'
import { DispatchConcurrencyEditor } from './DispatchConcurrencyEditor'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

// CARD-0506 test design F-1/F-2 fixtures, built locally with hand-written expected values: G7
// (global revision 7), G-empty, P0 (project without a row), P3/P4 (project overrides), P5/G8 (the
// conflict refresh) and one history row r3.
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

type RoleRow = DispatchConcurrencyEffectiveDto['roles'][number]

function defaultRole(role: string): RoleRow {
  return { role, maxParallel: 2, maxParallelSource: 'default', maxQueued: null, maxQueuedSource: 'default', combinedParallel: 2 }
}

function defaultEffective(globalRevision: number): DispatchConcurrencyEffectiveDto {
  return {
    mode: 'LegacyOpen',
    modeSource: 'default',
    maxParallel: 6,
    maxParallelSource: 'default',
    maxQueued: null,
    maxQueuedSource: 'default',
    roles: ROLES.map(defaultRole),
    globalRevision,
    projectRevision: 0,
  }
}

/** G7 effective: overall 4/12 global; Code parallel 3 global; Review queued 5 global; others default. */
function g7Effective(globalRevision: number, projectRevision: number, codeParallel = 3): DispatchConcurrencyEffectiveDto {
  return {
    mode: 'LegacyOpen',
    modeSource: 'default',
    maxParallel: 4,
    maxParallelSource: 'global',
    maxQueued: 12,
    maxQueuedSource: 'global',
    roles: ROLES.map((role) =>
      role === 'Code'
        ? { role, maxParallel: codeParallel, maxParallelSource: 'global', maxQueued: null, maxQueuedSource: 'default', combinedParallel: codeParallel }
        : role === 'Review'
          ? { role, maxParallel: 2, maxParallelSource: 'default', maxQueued: 5, maxQueuedSource: 'global', combinedParallel: 2 }
          : defaultRole(role),
    ),
    globalRevision,
    projectRevision,
  }
}

function withRole(effective: DispatchConcurrencyEffectiveDto, role: string, patch: Partial<RoleRow>): DispatchConcurrencyEffectiveDto {
  return { ...effective, roles: effective.roles.map((row) => (row.role === role ? { ...row, ...patch } : row)) }
}

function occupancyFor(effective: DispatchConcurrencyEffectiveDto): DispatchConcurrencyOccupancyDto {
  return {
    open: 3,
    parallel: 2,
    queued: 1,
    parallelRemaining: effective.maxParallel - 2,
    queuedRemaining: effective.maxQueued === null ? null : effective.maxQueued - 1,
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

function global(revision: number, overrides: Record<string, unknown>, effective: DispatchConcurrencyEffectiveDto): DispatchConcurrencyGlobalDto {
  return {
    seed: SEED,
    overrides,
    effective,
    nullProject: effective,
    supportedRoles: ROLES,
    ranges: RANGES,
    revision,
    updatedAt: '2026-10-13T10:05:00Z',
    reason: 'operator',
    provenance: 'Human',
    callerTaskId: null,
    occupancy: occupancyFor(effective),
  }
}

const G7_OVERRIDES = { schemaVersion: 1, maxParallel: 4, maxQueued: 12, roles: { Code: { maxParallel: 3 }, Review: { maxQueued: 5 } } }
const G7 = global(7, G7_OVERRIDES, g7Effective(7, 0))
const G_EMPTY = global(7, { schemaVersion: 1 }, defaultEffective(7))

function project(
  revision: number,
  globalRevision: number,
  overrides: Record<string, unknown>,
  inherited: DispatchConcurrencyEffectiveDto,
  effective: DispatchConcurrencyEffectiveDto,
  extra: Partial<DispatchConcurrencyProjectDto> = {},
): DispatchConcurrencyProjectDto {
  return {
    projectId: P,
    overrides,
    inherited,
    effective,
    supportedRoles: ROLES,
    ranges: RANGES,
    revision,
    globalRevision,
    updatedAt: revision === 0 ? null : '2026-10-13T10:10:00Z',
    reason: revision === 0 ? null : 'project operator',
    provenance: revision === 0 ? null : 'Human',
    callerTaskId: null,
    occupancy: occupancyFor(effective),
    ...extra,
  }
}

const P0 = project(0, 7, { schemaVersion: 1 }, g7Effective(7, 0), g7Effective(7, 0))
const P3 = project(3, 7, { schemaVersion: 1, roles: { Code: { maxParallel: 1 } } }, g7Effective(7, 3),
  withRole(g7Effective(7, 3), 'Code', { maxParallel: 1, maxParallelSource: 'project', combinedParallel: 1 }))
function p4Effective(globalRevision: number): DispatchConcurrencyEffectiveDto {
  return withRole(
    withRole(g7Effective(globalRevision, 4), 'Code', { maxParallel: 1, maxParallelSource: 'project', combinedParallel: 1 }),
    'Review', { maxQueued: null, maxQueuedSource: 'project' },
  )
}
const P4_OVERRIDES = { schemaVersion: 1, roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } }
const P4 = project(4, 7, P4_OVERRIDES, g7Effective(7, 4), p4Effective(7))
/** Conflict refresh: another operator saved project r5 with Code parallel 3 against global r8. */
const P5 = project(5, 8, { schemaVersion: 1, roles: { Code: { maxParallel: 3 } } }, g7Effective(8, 5),
  withRole(g7Effective(8, 5), 'Code', { maxParallel: 3, maxParallelSource: 'project', combinedParallel: 3 }),
  { reason: 'another operator' })

const HISTORY_R3: DispatchConcurrencyRevisionPageDto = {
  revisions: [
    {
      revision: 3,
      previousRevision: 2,
      snapshot: { scopeKey: P, projectId: P, revision: 3, provenance: 'Human', reason: 'earlier', callerTaskId: null, overrides: { schemaVersion: 1 } },
      createdAt: '2026-10-12T09:00:00Z',
      reason: 'earlier',
      provenance: 'Human',
      callerTaskId: null,
    },
  ],
  nextBeforeRevision: null,
}
const HISTORY_EMPTY: DispatchConcurrencyRevisionPageDto = { revisions: [], nextBeforeRevision: null }

const F409 = (globalRevision: number, projectRevision: number) =>
  HttpResponse.json(
    {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
      title: 'Conflict',
      status: 409,
      detail: `Dispatch concurrency revision does not match. Global revision is ${globalRevision}; project revision is ${projectRevision}.`,
      traceId: 'c506-conflict',
      code: 'dispatch_concurrency_revision_conflict',
      globalRevision,
      projectRevision,
    },
    { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
  )

const F422 = (errors: Record<string, string[]>) =>
  HttpResponse.json(
    {
      type: 'https://tools.ietf.org/html/rfc4918#section-11.2',
      title: 'Unprocessable Entity',
      status: 422,
      detail: 'One or more validation errors occurred.',
      traceId: 'c506-validation',
      code: 'validation_failed',
      errors,
    },
    { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
  )

type Put = { raw: string; body: Record<string, unknown> }
type GetReply = DispatchConcurrencyScopeDto | Response | Promise<Response>

/**
 * MSW for one scope. `state.get` is the current GET reply (tests swap it to model another writer);
 * `put` answers each PUT. Counts are request counts as the handlers saw them.
 */
type PutReply = (put: Put, index: number) => Response | Promise<Response>

function serve(projectId: string | null, initial: GetReply, put?: PutReply, history = HISTORY_R3) {
  const state = { get: initial as GetReply, gets: 0, puts: [] as Put[], put }
  const base = projectId ? '/api/projects/:projectId/dispatch-concurrency' : '/api/dispatch-concurrency'
  server.use(
    http.get(base, ({ params }) => {
      if (projectId) expect(params.projectId).toBe(projectId)
      state.gets += 1
      const reply = state.get
      return reply instanceof Response || reply instanceof Promise ? reply : HttpResponse.json(reply)
    }),
    http.get(`${base}/revisions`, () => HttpResponse.json(history)),
    http.put(base, async ({ request, params }) => {
      if (projectId) expect(params.projectId).toBe(projectId)
      const raw = await request.text()
      const entry = { raw, body: JSON.parse(raw) as Record<string, unknown> }
      state.puts.push(entry)
      if (!state.put) return HttpResponse.json({ error: 'unexpected PUT' }, { status: 500 })
      return state.put(entry, state.puts.length - 1)
    }),
  )
  return state
}

/** A successful PUT: the server's document becomes `schemaVersion` plus the sent overrides, revision + 1. */
function accept<T extends DispatchConcurrencyScopeDto>(state: { get: GetReply }, from: T) {
  let current = from
  return (put: Put) => {
    current = { ...current, revision: current.revision + 1, overrides: { schemaVersion: 1, ...(put.body.overrides as object) } }
    state.get = current
    return HttpResponse.json(current)
  }
}

function hold() {
  let release!: () => void
  const gate = new Promise<void>((resolve) => { release = resolve })
  return { gate, release }
}

const shown = () => vi.mocked(notifications.show).mock.calls.map(([options]) => options as { color?: string; message?: unknown })
const successes = () => shown().filter((options) => options.color === 'green')

beforeEach(() => {
  vi.mocked(notifications.show).mockClear()
})

function sw(name: string) {
  return screen.getByRole('switch', { name })
}
function num(name: string) {
  return screen.getByRole('textbox', { name })
}
function saveButton() {
  return screen.getByTestId('dispatch-concurrency-save')
}

async function setValue(name: string, value: string) {
  const input = num(name)
  await userEvent.clear(input)
  if (value) await userEvent.type(input, value)
}

async function typeReason(reason: string) {
  await userEvent.type(screen.getByTestId('dispatch-concurrency-reason'), reason)
}

async function loaded() {
  await screen.findByTestId('dispatch-concurrency-save')
}

/** Settle pending timers and microtasks so a write that would fire on its own has done so. */
async function flush() {
  for (let i = 0; i < 5; i += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0))
    })
  }
}

function LiveEditor({ connection, projectId }: { connection: { current: HubConnection | null }; projectId: string | null }) {
  useSignalRInvalidation(connection)
  return <DispatchConcurrencyEditor projectId={projectId} />
}

function fakeHub() {
  const callbacks = new Map<string, (payload: object) => void>()
  const connection = {
    on: (event: string, callback: (payload: object) => void) => callbacks.set(event, callback),
    off: vi.fn(),
  }
  return { ref: { current: connection as unknown as HubConnection }, emit: (event: string, payload: object) => act(() => callbacks.get(event)!(payload)) }
}

// ---- V-8 projection: rows from supportedRoles, inherited values and source labels, read-only mode ----

it('C506 V08 rows-and-sources', async () => {
  // Code is a project "No limit" override: the role value is null, the effective cap is the overall 4.
  const dto = project(4, 7, { schemaVersion: 1, roles: { Code: { maxParallel: null }, Review: { maxQueued: null } } }, g7Effective(7, 4),
    withRole(withRole(g7Effective(7, 4), 'Code', { maxParallel: null, maxParallelSource: 'project', combinedParallel: 4 }),
      'Review', { maxQueued: null, maxQueuedSource: 'project' }))
  serve(P, { ...dto, occupancy: { ...dto.occupancy, queuedRemaining: null } })
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  expect(screen.getAllByTestId(/^dispatch-concurrency-row-/)).toHaveLength(15)
  expect(screen.getByTestId('dispatch-concurrency-row-overall')).toHaveTextContent('Overall')
  for (const role of ROLES) expect(screen.getByTestId(`dispatch-concurrency-row-${role}`)).toHaveTextContent(role)

  const codeInherited = screen.getByTestId('dispatch-concurrency-inherited-Code-maxParallel')
  expect(codeInherited).toHaveTextContent('3')
  expect(codeInherited).toHaveTextContent('Global override')
  expect(screen.getByTestId('dispatch-concurrency-inherited-Review-maxQueued')).toHaveTextContent('5')
  expect(screen.getByTestId('dispatch-concurrency-inherited-Review-maxQueued')).toHaveTextContent('Global override')
  expect(screen.getByTestId('dispatch-concurrency-inherited-Plan-maxParallel')).toHaveTextContent('2')
  expect(screen.getByTestId('dispatch-concurrency-inherited-Plan-maxParallel')).toHaveTextContent('Built-in default')
  expect(screen.getByTestId('dispatch-concurrency-inherited-overall-maxParallel')).toHaveTextContent('4')

  expect(sw('Code max parallel override')).toBeChecked()
  expect(screen.getByRole('checkbox', { name: 'Code max parallel no limit' })).toBeChecked()
  expect(sw('Plan max parallel override')).not.toBeChecked()
  expect(screen.getByTestId('dispatch-concurrency-effective-parallel-Code')).toHaveTextContent(/^4$/)
  expect(screen.getByTestId('dispatch-concurrency-effective-Code')).toHaveTextContent('Project override')

  expect(screen.getByTestId('dispatch-concurrency-mode')).toHaveTextContent('LegacyOpen')
  expect(screen.queryByRole('combobox', { name: /mode/i })).toBeNull()
  expect(screen.queryByRole('textbox', { name: /mode/i })).toBeNull()
  const occupancy = screen.getByTestId('dispatch-concurrency-occupancy')
  expect(occupancy).toHaveTextContent('Open 3 · running 2 · queued 1 · parallel remaining 2 · queued remaining ∞')

  const history = await screen.findByTestId('dispatch-concurrency-history')
  expect(await within(history).findByText(/r3/)).toHaveTextContent('r3 · Human · earlier · 2026-10-12T09:00:00Z')
  expect(within(history).queryByRole('button')).toBeNull()
  expect(screen.queryByRole('button', { name: /restore/i })).toBeNull()
})

it('C506 V08 dto-only-role', async () => {
  const roles = [...ROLES, 'FutureRole']
  const inherited = { ...g7Effective(7, 0), roles: [...g7Effective(7, 0).roles, defaultRole('FutureRole')] }
  const dto: DispatchConcurrencyProjectDto = { ...P0, supportedRoles: roles, inherited, effective: inherited }
  serve(P, dto)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  expect(screen.getAllByTestId(/^dispatch-concurrency-row-/)).toHaveLength(16)
  expect(screen.getByTestId('dispatch-concurrency-row-FutureRole')).toHaveTextContent('FutureRole')
  expect(sw('FutureRole max parallel override')).not.toBeChecked()
  expect(screen.getByTestId('dispatch-concurrency-inherited-FutureRole-maxParallel')).toHaveTextContent('2')
})

it('C506 V08 unknown-source', async () => {
  const inherited = withRole(g7Effective(7, 0), 'Code', { maxParallelSource: 'scheduled' })
  serve(P, { ...P0, inherited, effective: inherited })
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  const cell = screen.getByTestId('dispatch-concurrency-inherited-Code-maxParallel')
  expect(cell).toHaveTextContent('scheduled')
  expect(cell).not.toHaveTextContent('Built-in default')
  expect(cell).not.toHaveTextContent('Global override')
  expect(screen.getByTestId('dispatch-concurrency-subtitle')).toHaveTextContent('Inheriting global defaults; no project overrides')
})

// ---- V-9 sparse Save: only the operator's override, integer gate, pending gate, offline retry ----

it('C506 V09 sparse-project', async () => {
  const state = serve(P, P0)
  state.put = accept(state, P0)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  expect(num('Code max parallel')).toHaveValue('3')
  await setValue('Code max parallel', '1')
  await typeReason('tighten code')
  await userEvent.click(saveButton())

  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body).toStrictEqual({
    expectedRevision: 0,
    expectedGlobalRevision: 7,
    overrides: { roles: { Code: { maxParallel: 1 } } },
    reason: 'tighten code',
    provenance: 'Human',
  })
  expect(state.puts[0].raw).toContain('"maxParallel":1')
  await waitFor(() => expect(successes().map((options) => options.message)).toStrictEqual(['Saved revision 1']))
})

it('C506 V09 sparse-global', async () => {
  const state = serve(null, G_EMPTY)
  state.put = accept(state, G_EMPTY)
  renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
  await loaded()

  expect(screen.getByTestId('dispatch-concurrency-inherited-Code-maxParallel')).toHaveTextContent('Built-in default')
  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('global code')
  await userEvent.click(saveButton())

  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body).toStrictEqual({
    expectedRevision: 7,
    expectedGlobalRevision: 7,
    overrides: { roles: { Code: { maxParallel: 1 } } },
    reason: 'global code',
    provenance: 'Human',
  })
})

it('C506 V09 trim-reason', async () => {
  const state = serve(P, P0)
  state.put = accept(state, P0)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('   padded reason   ')
  await userEvent.click(saveButton())

  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body.reason).toBe('padded reason')
})

it('C506 V09 invalid-disabled', async () => {
  const state = serve(P, P0)
  state.put = accept(state, P0)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await typeReason('valid reason')
  expect(saveButton()).toBeEnabled()
  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '')
  expect(saveButton()).toBeDisabled()
  await setValue('Code max parallel', '1.5')
  expect(saveButton()).toBeDisabled()
  await setValue('Code max parallel', '513')
  expect(saveButton()).toBeDisabled()
  await userEvent.click(saveButton())
  await flush()
  expect(state.puts).toHaveLength(0)

  await setValue('Code max parallel', '2')
  expect(saveButton()).toBeEnabled()
})

it('C506 V09 double-submit', async () => {
  const { gate, release } = hold()
  const state = serve(P, P0, async (put) => {
    await gate
    return accept(state, P0)(put)
  })
  try {
    renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
    await loaded()
    await userEvent.click(sw('Code max parallel override'))
    await setValue('Code max parallel', '1')
    await typeReason('once')
    await userEvent.dblClick(saveButton())
    await userEvent.click(saveButton())
    await waitFor(() => expect(state.puts).toHaveLength(1))
    await flush()
    expect(state.puts).toHaveLength(1)
    expect(saveButton()).toBeDisabled()
  } finally {
    release()
  }
  await waitFor(() => expect(successes()).toHaveLength(1))
  expect(state.puts).toHaveLength(1)
})

it('C506 V09 offline', async () => {
  const state = serve(P, P0, () => HttpResponse.error())
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('offline attempt')
  await userEvent.click(saveButton())

  expect(await screen.findByTestId('dispatch-concurrency-error')).toBeInTheDocument()
  expect(state.puts).toHaveLength(1)
  expect(num('Code max parallel')).toHaveValue('1')
  expect(screen.getByTestId('dispatch-concurrency-reason')).toHaveValue('offline attempt')
  expect(successes()).toHaveLength(0)
  expect(shown().some((options) => options.color === 'red')).toBe(true)

  expect(saveButton()).toBeEnabled()
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(2))
  expect(state.puts[1].body.overrides).toStrictEqual({ roles: { Code: { maxParallel: 1 } } })
  expect(successes()).toHaveLength(0)
})

// ---- V-10 No limit sends explicit null; Revert omits the row, keeps unrelated overrides ----

async function noLimitThenRevert(row: 'Overall' | 'Review', field: 'max parallel' | 'max queued', expected: Record<string, unknown>) {
  const state = serve(P, P3)
  state.put = accept(state, P3)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw(`${row} ${field} override`))
  await userEvent.click(screen.getByRole('checkbox', { name: `${row} ${field} no limit` }))
  await typeReason('unbounded')
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body.overrides).toStrictEqual(expected)
  expect(state.puts[0].raw).toContain(':null')
  await waitFor(() => expect(successes()).toHaveLength(1))

  await userEvent.click(screen.getByRole('button', { name: `Revert ${row} to inherited` }))
  expect(sw(`${row} ${field} override`)).not.toBeChecked()
  await typeReason('back to inherited')
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(2))
  expect(state.puts[1].body.overrides).toStrictEqual({ roles: { Code: { maxParallel: 1 } } })
  return state
}

it('C506 V10 overall-queued', async () => {
  await noLimitThenRevert('Overall', 'max queued', { maxQueued: null, roles: { Code: { maxParallel: 1 } } })
  expect(screen.getByTestId('dispatch-concurrency-inherited-overall-maxQueued')).toHaveTextContent('12')
  expect(screen.getByTestId('dispatch-concurrency-inherited-overall-maxQueued')).toHaveTextContent('Global override')
  // Overall max parallel cannot be unbounded: its override offers no "No limit".
  await userEvent.click(sw('Overall max parallel override'))
  expect(num('Overall max parallel')).toHaveValue('4')
  expect(screen.queryByRole('checkbox', { name: 'Overall max parallel no limit' })).toBeNull()
})

it('C506 V10 review-parallel', async () => {
  await noLimitThenRevert('Review', 'max parallel', { roles: { Code: { maxParallel: 1 }, Review: { maxParallel: null } } })
  expect(screen.getByTestId('dispatch-concurrency-inherited-Review-maxParallel')).toHaveTextContent('2')
  expect(screen.getByTestId('dispatch-concurrency-inherited-Review-maxParallel')).toHaveTextContent('Built-in default')
})

it('C506 V10 review-queued', async () => {
  const state = await noLimitThenRevert('Review', 'max queued', { roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } })
  expect(Object.hasOwn(state.puts[1].body.overrides as object, 'roles')).toBe(true)
  expect(Object.hasOwn((state.puts[1].body.overrides as { roles: object }).roles, 'Review')).toBe(false)
  expect(screen.getByTestId('dispatch-concurrency-inherited-Review-maxQueued')).toHaveTextContent('5')
  expect(screen.getByTestId('dispatch-concurrency-inherited-Review-maxQueued')).toHaveTextContent('Global override')
})

// ---- V-11 Clear all overrides: confirm first, exact {} body, disabled when nothing to clear ----

it('C506 V11 global', async () => {
  const withMode = global(7, { ...G7_OVERRIDES, mode: 'SeparateQueues' }, g7Effective(7, 0))
  const state = serve(null, withMode)
  state.put = accept(state, withMode)
  renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
  await loaded()

  await typeReason('reset global')
  await userEvent.click(screen.getByRole('button', { name: 'Clear all overrides' }))
  expect(await screen.findByTestId('dispatch-concurrency-clear-confirm')).toBeInTheDocument()
  await flush()
  expect(state.puts).toHaveLength(0)
  await userEvent.click(screen.getByRole('button', { name: 'Clear overrides' }))
  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body).toStrictEqual({
    expectedRevision: 7,
    expectedGlobalRevision: 7,
    overrides: {},
    reason: 'reset global',
    provenance: 'Human',
  })
  expect(state.puts[0].raw).toContain('"overrides":{}')
})

it('C506 V11 project', async () => {
  const state = serve(P, P4)
  state.put = accept(state, P4)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await typeReason('reset project')
  await userEvent.click(screen.getByRole('button', { name: 'Clear all overrides' }))
  await flush()
  expect(state.puts).toHaveLength(0)
  await userEvent.click(screen.getByRole('button', { name: 'Clear overrides' }))
  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body).toStrictEqual({
    expectedRevision: 4,
    expectedGlobalRevision: 7,
    overrides: {},
    reason: 'reset project',
    provenance: 'Human',
  })
  await waitFor(() => expect(sw('Code max parallel override')).not.toBeChecked())
})

it('C506 V11 schema-only-disabled', async () => {
  serve(P, P0)
  const first = renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()
  await typeReason('nothing to clear')
  expect(screen.getByRole('button', { name: 'Clear all overrides' })).toBeDisabled()
  first.unmount()

  serve(P, project(2, 7, { schemaVersion: 1, mode: 'SeparateQueues' }, g7Effective(7, 2), { ...g7Effective(7, 2), mode: 'SeparateQueues', modeSource: 'project' }))
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()
  await typeReason('mode only')
  expect(screen.getByRole('button', { name: 'Clear all overrides' })).toBeEnabled()
})

it('C506 V11 cancel', async () => {
  const state = serve(P, P4)
  state.put = accept(state, P4)
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await setValue('Code max parallel', '2')
  await typeReason('keep me')
  await userEvent.click(screen.getByRole('button', { name: 'Clear all overrides' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByTestId('dispatch-concurrency-clear-confirm')).toBeNull())
  await flush()
  expect(state.puts).toHaveLength(0)
  expect(num('Code max parallel')).toHaveValue('2')
  expect(screen.getByTestId('dispatch-concurrency-reason')).toHaveValue('keep me')
})

// ---- V-12 conflict and live refresh: keep the draft, refetch, show the diff, never auto-retry ----

it('C506 V12 conflict-project', async () => {
  const state = serve(P, P0, (put, index) => {
    if (index === 0) {
      state.get = P5
      return F409(8, 5)
    }
    return accept(state, P5)(put)
  })
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('tighten')
  await userEvent.click(saveButton())

  const alert = await screen.findByTestId('dispatch-concurrency-conflict')
  expect(alert).toHaveAttribute('role', 'alert')
  expect(alert).toHaveTextContent('Saved revision changed (now global r8, project r5)')
  expect(alert).toHaveTextContent('Code max parallel: your draft 1, now saved 3')
  expect(state.gets).toBe(2)
  expect(state.puts).toHaveLength(1)
  expect(num('Code max parallel')).toHaveValue('1')
  expect(screen.getByTestId('dispatch-concurrency-reason')).toHaveValue('tighten')

  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(2))
  expect(state.puts[1].body).toStrictEqual({
    expectedRevision: 5,
    expectedGlobalRevision: 8,
    overrides: { roles: { Code: { maxParallel: 1 } } },
    reason: 'tighten',
    provenance: 'Human',
  })
})

it('C506 V12 conflict-global', async () => {
  const g8 = global(8, G7_OVERRIDES, g7Effective(8, 0))
  const state = serve(null, G7, (put, index) => {
    if (index === 0) {
      state.get = g8
      return F409(8, 0)
    }
    return accept(state, g8)(put)
  })
  renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
  await loaded()

  await setValue('Code max parallel', '1')
  await typeReason('global tighten')
  await userEvent.click(saveButton())

  const alert = await screen.findByTestId('dispatch-concurrency-conflict')
  expect(alert).toHaveTextContent('Saved revision changed (now global r8, project r0)')
  expect(alert).toHaveTextContent('Code max parallel: your draft 1, now saved 3')
  await waitFor(() => expect(state.gets).toBe(2))
  await flush()
  expect(state.puts).toHaveLength(1)

  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(2))
  expect(state.puts[1].body.expectedRevision).toBe(8)
  expect(state.puts[1].body.expectedGlobalRevision).toBe(8)
})

it('C506 V12 refresh-fails', async () => {
  const state = serve(P, P0, (put, index) => {
    if (index === 0) {
      state.get = new HttpResponse(null, { status: 503 })
      return F409(8, 5)
    }
    return accept(state, P5)(put)
  })
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('tighten')
  await userEvent.click(saveButton())

  expect(await screen.findByTestId('dispatch-concurrency-refresh-failed')).toBeInTheDocument()
  expect(state.gets).toBe(2)
  expect(saveButton()).toBeDisabled()
  await userEvent.click(saveButton())
  await flush()
  expect(state.puts).toHaveLength(1)
  expect(num('Code max parallel')).toHaveValue('1')
  expect(screen.getByTestId('dispatch-concurrency-reason')).toHaveValue('tighten')

  state.get = P5
  await userEvent.click(screen.getByRole('button', { name: 'Reload saved settings' }))
  expect(await screen.findByTestId('dispatch-concurrency-conflict')).toHaveTextContent('now global r8, project r5')
  expect(saveButton()).toBeEnabled()
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(2))
  expect(state.puts[1].body.expectedRevision).toBe(5)
})

it('C506 V12 live-dirty', async () => {
  const hub = fakeHub()
  const state = serve(P, P0, (put) => accept(state, P5)(put))
  renderWithProviders(<LiveEditor connection={hub.ref} projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('editing')
  state.get = P5
  hub.emit('DispatchConcurrencyChanged', { scope: P, revision: 5, globalRevision: 8, projectRevision: 5 })

  const alert = await screen.findByTestId('dispatch-concurrency-conflict')
  expect(alert).toHaveTextContent('Saved revision changed (now global r8, project r5)')
  expect(alert).toHaveTextContent('Code max parallel: your draft 1, now saved 3')
  expect(state.gets).toBe(2)
  expect(num('Code max parallel')).toHaveValue('1')
  expect(screen.getByTestId('dispatch-concurrency-reason')).toHaveValue('editing')

  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body.expectedRevision).toBe(5)
  expect(state.puts[0].body.expectedGlobalRevision).toBe(8)
})

it('C506 V12 live-clean', async () => {
  const hub = fakeHub()
  const state = serve(P, P0, (put) => accept(state, P5)(put))
  renderWithProviders(<LiveEditor connection={hub.ref} projectId={P} />)
  await loaded()
  expect(sw('Code max parallel override')).not.toBeChecked()

  state.get = P5
  hub.emit('DispatchConcurrencyChanged', { scope: P, revision: 5, globalRevision: 8, projectRevision: 5 })

  await waitFor(() => expect(sw('Code max parallel override')).toBeChecked())
  expect(num('Code max parallel')).toHaveValue('3')
  expect(screen.queryByTestId('dispatch-concurrency-conflict')).toBeNull()

  await typeReason('after refresh')
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body).toStrictEqual({
    expectedRevision: 5,
    expectedGlobalRevision: 8,
    overrides: { roles: { Code: { maxParallel: 3 } } },
    reason: 'after refresh',
    provenance: 'Human',
  })
})

it('C506 V12 inherited-change', async () => {
  const hub = fakeHub()
  const state = serve(P, P4)
  state.put = accept(state, P4)
  renderWithProviders(<LiveEditor connection={hub.ref} projectId={P} />)
  await loaded()
  expect(screen.getByTestId('dispatch-concurrency-inherited-Code-maxParallel')).toHaveTextContent('3')

  // Global r8 lowers the inherited Code limit 3 → 2; the project row (r4, Code 1) is unchanged.
  state.get = project(4, 8, P4_OVERRIDES, g7Effective(8, 4, 2), withRole(p4Effective(8), 'Code', { maxParallel: 1, maxParallelSource: 'project', combinedParallel: 1 }))
  hub.emit('DispatchConcurrencyChanged', { scope: 'global', revision: 8, globalRevision: 8, projectRevision: 0 })

  await waitFor(() => expect(screen.getByTestId('dispatch-concurrency-inherited-Code-maxParallel')).toHaveTextContent('2'))
  expect(sw('Code max parallel override')).toBeChecked()
  expect(num('Code max parallel')).toHaveValue('1')
  expect(screen.getByTestId('dispatch-concurrency-effective-parallel-Code')).toHaveTextContent(/^1$/)
  expect(screen.getByTestId('dispatch-concurrency-effective-Code')).toHaveTextContent('Project override')

  await typeReason('keep code')
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(1))
  expect(state.puts[0].body).toStrictEqual({
    expectedRevision: 4,
    expectedGlobalRevision: 8,
    overrides: { roles: { Code: { maxParallel: 1 }, Review: { maxQueued: null } } },
    reason: 'keep code',
    provenance: 'Human',
  })
})

// ---- V-13 server refusal: field error on the input, alert, draft kept, no success ----

it('C506 V13 field-error', async () => {
  const state = serve(P, P0, () => F422({ 'roles.Code.maxParallel': ['roles.Code.maxParallel must be an integer from 1 to 512.'] }))
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('Code max parallel override'))
  await setValue('Code max parallel', '1')
  await typeReason('server says no')
  await userEvent.click(saveButton())

  const alert = await screen.findByTestId('dispatch-concurrency-error')
  expect(alert).toHaveAttribute('role', 'alert')
  expect(alert).toHaveTextContent('roles.Code.maxParallel must be an integer from 1 to 512.')
  const input = num('Code max parallel')
  expect(input).toHaveAttribute('aria-invalid', 'true')
  expect(input).toHaveAccessibleDescription(/roles\.Code\.maxParallel must be an integer from 1 to 512\./)
  expect(input).toHaveValue('1')
  expect(state.puts).toHaveLength(1)
  expect(successes()).toHaveLength(0)
})

it('C506 V13 unknown-role', async () => {
  const roles = [...ROLES, 'FutureRole']
  const inherited = { ...g7Effective(7, 0), roles: [...g7Effective(7, 0).roles, defaultRole('FutureRole')] }
  const state = serve(P, { ...P0, supportedRoles: roles, inherited, effective: inherited },
    () => F422({ roles: ["Unknown role 'FutureRole'."] }))
  renderWithProviders(<DispatchConcurrencyEditor projectId={P} />)
  await loaded()

  await userEvent.click(sw('FutureRole max parallel override'))
  await setValue('FutureRole max parallel', '2')
  await typeReason('version skew')
  await userEvent.click(saveButton())

  expect(await screen.findByTestId('dispatch-concurrency-error')).toHaveTextContent("Unknown role 'FutureRole'.")
  expect(state.puts).toHaveLength(1)
  expect(state.puts[0].body.overrides).toStrictEqual({ roles: { FutureRole: { maxParallel: 2 } } })
  expect(num('FutureRole max parallel')).toHaveValue('2')
  await flush()
  expect(state.puts).toHaveLength(1)
  expect(successes()).toHaveLength(0)
})

// ---- V-14 loading, load failure with Retry, no-op versus saved, empty history ----

it('C506 V14 loading-retry', async () => {
  const { gate, release } = hold()
  const state = serve(null, (async () => {
    await gate
    return new HttpResponse(null, { status: 503 })
  })())
  try {
    renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
    expect(await screen.findByTestId('dispatch-concurrency-loading')).toBeInTheDocument()
    expect(screen.queryByTestId('dispatch-concurrency-save')).toBeNull()
  } finally {
    release()
  }
  const retry = await screen.findByRole('button', { name: 'Retry' })
  expect(screen.getByTestId('dispatch-concurrency-load-error')).toBeInTheDocument()
  expect(screen.queryByTestId('dispatch-concurrency-save')).toBeNull()

  state.get = G7
  await userEvent.click(retry)
  await loaded()
  expect(state.gets).toBe(2)
  expect(screen.getByTestId('dispatch-concurrency-inherited-Code-maxParallel')).toHaveTextContent('2')
})

it('C506 V14 no-change', async () => {
  const state = serve(null, G7, () => HttpResponse.json(G7))
  renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
  await loaded()

  await typeReason('same again')
  await userEvent.click(saveButton())
  await waitFor(() => expect(successes()).toHaveLength(1))
  expect(successes()[0].message).toBe('No change')
  expect(state.puts[0].body.expectedRevision).toBe(7)
})

it('C506 V14 saved', async () => {
  const state = serve(null, G7)
  state.put = accept(state, G7)
  renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
  await loaded()

  await typeReason('touch')
  await userEvent.click(saveButton())
  await waitFor(() => expect(successes()).toHaveLength(1))
  expect(successes()[0].message).toBe('Saved revision 8')
  expect(screen.getByTestId('dispatch-concurrency-reason')).toHaveValue('')
})

it('C506 V14 history-empty', async () => {
  const state = serve(null, G7, undefined, HISTORY_EMPTY)
  state.put = accept(state, G7)
  renderWithProviders(<DispatchConcurrencyEditor projectId={null} />)
  await loaded()

  const history = screen.getByTestId('dispatch-concurrency-history')
  expect(await within(history).findByText('No revisions yet.')).toBeInTheDocument()
  expect(within(history).queryByRole('button')).toBeNull()
  expect(screen.queryByRole('button', { name: /restore/i })).toBeNull()
  await typeReason('usable')
  expect(saveButton()).toBeEnabled()
  await userEvent.click(saveButton())
  await waitFor(() => expect(state.puts).toHaveLength(1))
})
