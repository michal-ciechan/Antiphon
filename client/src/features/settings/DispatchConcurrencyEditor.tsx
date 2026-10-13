import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Group,
  List,
  Loader,
  Modal,
  NumberInput,
  Paper,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { ApiError, getApiErrorMessage, getApiFieldErrors } from '../../api/client'
import {
  isProjectScope,
  useDispatchConcurrency,
  useDispatchConcurrencyRevisions,
  usePutDispatchConcurrency,
  type DispatchConcurrencyEffectiveDto,
  type DispatchConcurrencyOverrides,
  type DispatchConcurrencyRevisionDto,
  type DispatchConcurrencyScopeDto,
} from '../../api/dispatchConcurrency'
import {
  diffDrafts,
  draftFromOverrides,
  isCleared,
  overridesFromDraft,
  sourceLabel,
  validateDraft,
  type ConcurrencyDraft,
  type DraftDiff,
  type LimitDraft,
  type RoleLimitDraft,
} from './dispatchConcurrencyModel'

type Field = 'maxParallel' | 'maxQueued'

const FIELD_LABEL: Record<Field, string> = { maxParallel: 'max parallel', maxQueued: 'max queued' }
const REVISION_CONFLICT = 'dispatch_concurrency_revision_conflict'
const UNSPECIFIED: LimitDraft = { specified: false, value: null }
const MODE_DESCRIPTION: Record<string, string> = {
  LegacyOpen: 'Max parallel limits open tasks when a task is created.',
  SeparateQueues: 'Max parallel limits running tasks at dispatch; max queued limits waiting tasks.',
}

interface Inherited {
  value: number | null
  source: string
}

/** Role names are server data: name-keyed reads stay own-property reads (Review ef9c0e6b F1). */
function own<T>(container: object, key: string): T | undefined {
  return Object.hasOwn(container, key) ? (container as Record<string, T>)[key] : undefined
}

function withOwn<T>(container: Record<string, T>, key: string, value: T): Record<string, T> {
  const next = { ...container }
  Object.defineProperty(next, key, { value, enumerable: true, writable: true, configurable: true })
  return next
}

function fmt(value: number | null | undefined): string {
  return value === null || value === undefined ? '∞' : String(value)
}

function fmtLimit(limit: LimitDraft): string {
  if (!limit.specified) return 'inherited'
  return limit.value === null ? 'no limit' : String(limit.value)
}

function revisionsOf(dto: DispatchConcurrencyScopeDto) {
  return isProjectScope(dto)
    ? { expectedRevision: dto.revision, expectedGlobalRevision: dto.globalRevision, global: dto.globalRevision, project: dto.revision }
    : { expectedRevision: dto.revision, expectedGlobalRevision: dto.revision, global: dto.revision, project: 0 }
}

function revisionKey(dto: DispatchConcurrencyScopeDto): string {
  const revisions = revisionsOf(dto)
  return `${revisions.global}:${revisions.project}`
}

/**
 * Both revisions only grow (the server writes `current + 1`), so a DTO with either revision below
 * the other's is older data: a cached GET that a failed or late refresh left behind a Save
 * (`C506 S2F2 refresh-fails`/`refresh-pending`).
 */
function isOlder(dto: DispatchConcurrencyScopeDto, than: DispatchConcurrencyScopeDto): boolean {
  const left = revisionsOf(dto)
  const right = revisionsOf(than)
  return left.global < right.global || left.project < right.project
}

function draftOf(dto: DispatchConcurrencyScopeDto): ConcurrencyDraft {
  return draftFromOverrides(dto.overrides, dto.supportedRoles)
}

/**
 * What a cell inherits when it has no override at this scope: the global effective values for a
 * project, the imported seed (source `default`) for the global scope.
 */
function inheritedFor(dto: DispatchConcurrencyScopeDto, role: string | null, field: Field): Inherited {
  if (isProjectScope(dto)) {
    const source = role === null ? dto.inherited : dto.inherited.roles.find((row) => row.role === role)
    if (!source) return { value: null, source: 'default' }
    return field === 'maxParallel'
      ? { value: source.maxParallel, source: source.maxParallelSource }
      : { value: source.maxQueued, source: source.maxQueuedSource }
  }
  const seed = role === null ? dto.seed : dto.seed.roles.find((row) => row.role === role)
  if (!seed) return { value: null, source: 'default' }
  return { value: field === 'maxParallel' ? seed.maxParallel : seed.maxQueued, source: 'default' }
}

/**
 * CARD-0506 D-3..D-11. One table, one draft, one Save for a single scope: `projectId` null edits the
 * global defaults, a project id edits that project's overrides. A load failure stays in this paper.
 */
export function DispatchConcurrencyEditor({ projectId }: { projectId: string | null }) {
  const scope = useDispatchConcurrency(projectId)
  const history = useDispatchConcurrencyRevisions(projectId)
  const title = projectId ? 'Project concurrency' : 'Global concurrency defaults'

  if (!scope.data) {
    return (
      <Paper withBorder p="md" data-testid="dispatch-concurrency-section">
        <Text fw={600}>{title}</Text>
        {scope.isError && !scope.isFetching ? (
          <Group gap="sm" data-testid="dispatch-concurrency-load-error">
            <Text size="sm" c="dimmed">
              Could not load concurrency settings.
            </Text>
            <Button size="compact-sm" variant="light" onClick={() => void scope.refetch()}>
              Retry
            </Button>
          </Group>
        ) : (
          <Group justify="center" py="sm" data-testid="dispatch-concurrency-loading">
            <Loader size="sm" />
          </Group>
        )}
      </Paper>
    )
  }

  return (
    <EditorForm
      // The draft belongs to one scope; a scope change starts a fresh form (D-7: keyed on scope only).
      key={projectId === null ? 'global' : `project:${projectId}`}
      projectId={projectId}
      title={title}
      latest={scope.data}
      refetch={async () => {
        const result = await scope.refetch()
        return result.isError || !result.data ? null : result.data
      }}
      history={history.data?.revisions ?? []}
      historyFailed={history.isError}
    />
  )
}

function EditorForm({
  projectId,
  title,
  latest,
  refetch,
  history,
  historyFailed,
}: {
  projectId: string | null
  title: string
  latest: DispatchConcurrencyScopeDto
  refetch: () => Promise<DispatchConcurrencyScopeDto | null>
  history: DispatchConcurrencyRevisionDto[]
  historyFailed: boolean
}) {
  const save = usePutDispatchConcurrency(projectId)
  // `base` is the newest saved state this form has seen (query data, Save response or conflict
  // refetch). The next Save targets it, and the saved values on screen (subtitle, mode, inherited,
  // effective, occupancy) are read from it (`C506 S2F2 refresh-fails`).
  const [base, setBase] = useState(latest)
  const [draft, setDraft] = useState(() => draftOf(latest))
  const [inputs, setInputs] = useState<Map<string, number | string>>(() => new Map())
  const [reason, setReason] = useState('')
  const [banner, setBanner] = useState(false)
  const [refresh, setRefresh] = useState<'idle' | 'refreshing' | 'failed'>('idle')
  const [submitting, setSubmitting] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Map<string, string>>(() => new Map())
  const [saveError, setSaveError] = useState<string | null>(null)
  const [confirmClear, setConfirmClear] = useState(false)

  const savedDraft = draftOf(base)
  const dirty = diffDrafts(draft, savedDraft).length > 0

  // Query data (SignalR or focus refetch, conflict refresh) that is not older than `base` replaces
  // it; older query data is ignored, so a failed or late refresh does not roll back a Save response
  // (`C506 S2F2 refresh-fails`/`refresh-pending`; `newer-after-save` keeps later writes). A newer revision is
  // adopted by a clean draft; a dirty draft is kept and the banner lists where it differs (D-7).
  // Same-revision data replaces `base` only (fresh occupancy). Adjusting state while rendering.
  if (latest !== base && !isOlder(latest, base)) {
    const newer = revisionKey(latest) !== revisionKey(base)
    setBase(latest)
    if (newer && dirty) {
      setBanner(true)
    } else if (newer) {
      setDraft(draftOf(latest))
      setInputs(new Map())
      setBanner(false)
    }
  }

  const errors = validateDraft(draft, base.ranges, reason)
  const reasonValid = !Object.hasOwn(errors, 'reason')
  // The single pending gate: Save, Clear, the limit switches, inputs and No limit boxes, Revert and
  // Reason share it, so no edit can be made during a submission for its response to discard
  // (`C506 S2F3 pending-save`/`pending-clear`/`pending-error`).
  const idle = !submitting && refresh === 'idle'
  const canSave = idle && Object.keys(errors).length === 0
  const canClear = idle && reasonValid && !isCleared(base.overrides)
  const bannerRevisions = revisionsOf(base)
  const bannerDiffs = banner ? diffDrafts(draft, savedDraft) : []
  const roles = [...new Set([...base.supportedRoles, ...Object.keys(draft.roles)])]

  const reloadAfterConflict = async () => {
    setRefresh('refreshing')
    const fresh = await refetch()
    if (!fresh) {
      setRefresh('failed')
      return
    }
    setBase(fresh)
    setBanner(true)
    setRefresh('idle')
  }

  const submit = async (overrides: DispatchConcurrencyOverrides) => {
    const sent = revisionsOf(base)
    setSubmitting(true)
    setSaveError(null)
    setFieldErrors(new Map())
    try {
      const result = await save.mutateAsync({
        expectedRevision: sent.expectedRevision,
        expectedGlobalRevision: sent.expectedGlobalRevision,
        overrides,
        reason,
      })
      setBase(result)
      setDraft(draftOf(result))
      setInputs(new Map())
      setReason('')
      setBanner(false)
      notifications.show({
        color: 'green',
        message: result.revision === sent.expectedRevision ? 'No change' : `Saved revision ${result.revision}`,
      })
    } catch (error) {
      const body = error instanceof ApiError ? error.body : null
      const code = body && typeof body === 'object' && Object.hasOwn(body, 'code') ? (body as { code: unknown }).code : null
      if (error instanceof ApiError && error.status === 409 && code === REVISION_CONFLICT) {
        await reloadAfterConflict()
      } else {
        const message = getApiErrorMessage(error, 'Could not save concurrency settings')
        setFieldErrors(new Map(Object.entries(getApiFieldErrors(error))))
        setSaveError(message)
        notifications.show({ color: 'red', message })
      }
    } finally {
      setSubmitting(false)
    }
  }

  const updateLimit = (role: string | null, field: Field, limit: LimitDraft, raw?: number | string) => {
    const path = role === null ? field : `roles.${role}.${field}`
    setInputs((previous) => {
      const next = new Map(previous)
      if (raw === undefined) next.delete(path)
      else next.set(path, raw)
      return next
    })
    setFieldErrors((previous) => {
      if (!previous.has(path)) return previous
      const next = new Map(previous)
      next.delete(path)
      return next
    })
    setDraft((previous) => {
      if (role === null) return { ...previous, [field]: limit }
      const row = own<RoleLimitDraft>(previous.roles, role) ?? { maxParallel: UNSPECIFIED, maxQueued: UNSPECIFIED }
      return { ...previous, roles: withOwn(previous.roles, role, { ...row, [field]: limit }) }
    })
  }

  const revertRow = (role: string | null) => {
    updateLimit(role, 'maxParallel', { ...UNSPECIFIED })
    updateLimit(role, 'maxQueued', { ...UNSPECIFIED })
  }

  const renderRow = (role: string | null) => {
    const rowLabel = role ?? 'Overall'
    const row: RoleLimitDraft =
      role === null
        ? { maxParallel: draft.maxParallel, maxQueued: draft.maxQueued }
        : own<RoleLimitDraft>(draft.roles, role) ?? { maxParallel: UNSPECIFIED, maxQueued: UNSPECIFIED }
    const cell = (field: Field) => {
      const path = role === null ? field : `roles.${role}.${field}`
      const isParallel = field === 'maxParallel'
      return (
        <LimitCell
          rowLabel={rowLabel}
          testKey={`${role ?? 'overall'}-${field}`}
          field={field}
          limit={row[field]}
          inherited={inheritedFor(base, role, field)}
          // Overall max parallel offers no "No limit" (D-3, server allowNull: false; `C506 V10 overall-queued`).
          nullable={!(role === null && isParallel)}
          min={isParallel ? base.ranges.parallelMin : base.ranges.queuedMin}
          max={isParallel ? base.ranges.parallelMax : base.ranges.queuedMax}
          raw={inputs.get(path)}
          error={fieldErrors.get(path) ?? own<string>(errors, path)}
          disabled={!idle}
          onChange={(limit, raw) => updateLimit(role, field, limit, raw)}
        />
      )
    }
    return (
      <Table.Tr key={role ?? '\u0000overall'} data-testid={`dispatch-concurrency-row-${role ?? 'overall'}`}>
        <Table.Td>
          <Text size="sm" fw={role === null ? 600 : undefined}>
            {rowLabel}
          </Text>
        </Table.Td>
        <Table.Td>{cell('maxParallel')}</Table.Td>
        <Table.Td>{cell('maxQueued')}</Table.Td>
        <Table.Td>
          <EffectiveCell testKey={role ?? 'overall'} effective={base.effective} role={role} />
        </Table.Td>
        <Table.Td>
          <Button
            size="compact-xs"
            variant="subtle"
            aria-label={`Revert ${rowLabel} to inherited`}
            disabled={!idle || (!row.maxParallel.specified && !row.maxQueued.specified)}
            onClick={() => revertRow(role)}
          >
            Revert
          </Button>
        </Table.Td>
      </Table.Tr>
    )
  }

  const occupancy = base.occupancy
  const project = isProjectScope(base)
  const subtitle =
    project && (base.revision === 0 || isCleared(base.overrides))
      ? 'Inheriting global defaults; no project overrides'
      : `Revision ${base.revision}${base.provenance ? ` · ${base.provenance}` : ''}${base.reason ? ` · ${base.reason}` : ''}`

  return (
    <Paper withBorder p="md" data-testid="dispatch-concurrency-section" aria-busy={submitting || refresh === 'refreshing'}>
      <Stack gap="sm">
        <div>
          <Text fw={600}>{title}</Text>
          <Text size="sm" c="dimmed" data-testid="dispatch-concurrency-subtitle">
            {subtitle}
          </Text>
        </div>
        <Group gap="xs" data-testid="dispatch-concurrency-mode">
          <Text size="sm">Mode</Text>
          <Badge variant="light">{base.effective.mode}</Badge>
          <Badge variant="light" color="gray">
            {sourceLabel(base.effective.modeSource)}
          </Badge>
          <Text size="xs" c="dimmed">
            {own<string>(MODE_DESCRIPTION, base.effective.mode) ?? ''}
          </Text>
        </Group>
        {banner && (
          <Alert color="red" role="alert" data-testid="dispatch-concurrency-conflict">
            <Text size="sm">
              Saved revision changed (now global r{bannerRevisions.global}, project r{bannerRevisions.project}). Your
              draft is kept; review it and save again.
            </Text>
            {bannerDiffs.length > 0 && (
              <List size="sm" mt={4}>
                {bannerDiffs.map((diff: DraftDiff) => (
                  <List.Item key={diff.path}>
                    {diff.label}: your draft {fmtLimit(diff.draft)}, now saved {fmtLimit(diff.saved)}
                  </List.Item>
                ))}
              </List>
            )}
          </Alert>
        )}
        {refresh === 'failed' && (
          <Alert color="red" role="alert" data-testid="dispatch-concurrency-refresh-failed">
            <Group gap="sm">
              <Text size="sm">The saved revision changed and could not be reloaded. Your draft is kept.</Text>
              <Button size="compact-sm" variant="light" onClick={() => void reloadAfterConflict()}>
                Reload saved settings
              </Button>
            </Group>
          </Alert>
        )}
        {saveError && (
          <Alert color="red" role="alert" data-testid="dispatch-concurrency-error">
            {saveError}
          </Alert>
        )}
        <Table.ScrollContainer minWidth={720}>
          <Table verticalSpacing="xs" data-testid="dispatch-concurrency-table">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Role</Table.Th>
                <Table.Th>Max parallel</Table.Th>
                <Table.Th>Max queued</Table.Th>
                <Table.Th>Effective</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {renderRow(null)}
              {roles.map((role) => renderRow(role))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <Text size="sm" c="dimmed" data-testid="dispatch-concurrency-occupancy">
          Open {occupancy.open} · running {occupancy.parallel} · queued {occupancy.queued} · parallel remaining{' '}
          {fmt(occupancy.parallelRemaining)} · queued remaining {fmt(occupancy.queuedRemaining)}
          {occupancy.exceedsBounds ? ' · over limit' : ''}
        </Text>
        <Group align="flex-end" wrap="wrap">
          <TextInput
            label="Reason"
            description="Required, up to 400 characters"
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
            disabled={!idle}
            w={{ base: '100%', sm: 320 }}
            data-testid="dispatch-concurrency-reason"
          />
          <Button
            data-testid="dispatch-concurrency-save"
            // One pending gate (`idle`), shared with Clear; no separate `loading` disable (test design G-30).
            disabled={!canSave}
            // The mode is read-only here (D-6): it is sent exactly as the targeted revision saved it,
            // present or absent, not as the draft was loaded (`C506 S2F1 live|conflict mode-*`).
            onClick={() => void submit(overridesFromDraft({ ...draft, mode: savedDraft.mode }))}
          >
            Save
          </Button>
          <Button
            variant="light"
            color="red"
            data-testid="dispatch-concurrency-clear"
            disabled={!canClear}
            onClick={() => setConfirmClear(true)}
          >
            Clear all overrides
          </Button>
        </Group>
        <Stack gap={4} data-testid="dispatch-concurrency-history">
          <Text size="sm" fw={600}>
            History
          </Text>
          {historyFailed && (
            <Text size="sm" c="dimmed">
              Could not load history.
            </Text>
          )}
          {!historyFailed && history.length === 0 && (
            <Text size="sm" c="dimmed">
              No revisions yet.
            </Text>
          )}
          {history.map((row) => (
            <Text key={row.revision} size="xs">
              r{row.revision} · {row.provenance} · {row.reason} · {row.createdAt}
            </Text>
          ))}
        </Stack>
      </Stack>
      <Modal opened={confirmClear} onClose={() => setConfirmClear(false)} title="Clear all overrides?">
        <Stack gap="sm" data-testid="dispatch-concurrency-clear-confirm">
          <Text size="sm">
            {project
              ? 'This project inherits every global default again.'
              : 'Every global limit returns to its built-in default.'}
          </Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setConfirmClear(false)}>
              Cancel
            </Button>
            <Button
              color="red"
              disabled={!canClear}
              onClick={() => {
                setConfirmClear(false)
                void submit({})
              }}
            >
              Clear overrides
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Paper>
  )
}

function LimitCell({
  rowLabel,
  testKey,
  field,
  limit,
  inherited,
  nullable,
  min,
  max,
  raw,
  error,
  disabled,
  onChange,
}: {
  rowLabel: string
  testKey: string
  field: Field
  limit: LimitDraft
  inherited: Inherited
  nullable: boolean
  min: number
  max: number
  raw: number | string | undefined
  error: string | undefined
  disabled: boolean
  onChange: (limit: LimitDraft, raw?: number | string) => void
}) {
  const name = `${rowLabel} ${FIELD_LABEL[field]}`
  const startValue = inherited.value ?? (nullable ? null : min)
  return (
    <Stack gap={4}>
      <Group gap={6} wrap="nowrap">
        <Switch
          size="xs"
          aria-label={`${name} override`}
          checked={limit.specified}
          disabled={disabled}
          onChange={(event) =>
            onChange(event.currentTarget.checked ? { specified: true, value: startValue } : { ...UNSPECIFIED })
          }
        />
        <Group gap={4} wrap="nowrap" data-testid={`dispatch-concurrency-inherited-${testKey}`}>
          <Text size="sm" c="dimmed">
            {fmt(inherited.value)}
          </Text>
          <Badge size="xs" variant="light" color="gray">
            {sourceLabel(inherited.source)}
          </Badge>
        </Group>
      </Group>
      {limit.specified && (
        <Group gap={6} wrap="nowrap" align="flex-start">
          <NumberInput
            size="xs"
            w={96}
            aria-label={name}
            value={limit.value === null ? '' : (raw ?? limit.value)}
            disabled={disabled || limit.value === null}
            min={min}
            max={max}
            clampBehavior="none"
            error={error}
            onChange={(value) =>
              onChange({ specified: true, value: typeof value === 'number' ? value : Number.NaN }, value)
            }
          />
          {nullable && (
            <Checkbox
              size="xs"
              mt={6}
              label="No limit"
              aria-label={`${name} no limit`}
              checked={limit.value === null}
              disabled={disabled}
              onChange={(event) =>
                onChange({ specified: true, value: event.currentTarget.checked ? null : (inherited.value ?? min) })
              }
            />
          )}
        </Group>
      )}
    </Stack>
  )
}

function EffectiveCell({
  testKey,
  effective,
  role,
}: {
  testKey: string
  effective: DispatchConcurrencyEffectiveDto
  role: string | null
}) {
  const row = role === null ? null : effective.roles.find((item) => item.role === role)
  if (role !== null && !row) {
    return (
      <Text size="sm" c="dimmed" data-testid={`dispatch-concurrency-effective-${testKey}`}>
        —
      </Text>
    )
  }
  // A role's effective parallel cap is combinedParallel: the lower of the role and overall limits (D-5).
  const parallel = row ? row.combinedParallel : effective.maxParallel
  const parallelSource = row ? row.maxParallelSource : effective.maxParallelSource
  const queued = row ? row.maxQueued : effective.maxQueued
  const queuedSource = row ? row.maxQueuedSource : effective.maxQueuedSource
  return (
    <Stack gap={2} data-testid={`dispatch-concurrency-effective-${testKey}`}>
      <Group gap={4} wrap="nowrap">
        <Text size="sm">Parallel</Text>
        <Text size="sm" fw={600} data-testid={`dispatch-concurrency-effective-parallel-${testKey}`}>
          {fmt(parallel)}
        </Text>
        {row && row.combinedParallel !== row.maxParallel && (
          <Text size="xs" c="dimmed">
            (role {fmt(row.maxParallel)}, overall cap)
          </Text>
        )}
        <Badge size="xs" variant="light" color="gray">
          {sourceLabel(parallelSource)}
        </Badge>
      </Group>
      <Group gap={4} wrap="nowrap">
        <Text size="sm">Queued</Text>
        <Text size="sm" fw={600}>
          {fmt(queued)}
        </Text>
        <Badge size="xs" variant="light" color="gray">
          {sourceLabel(queuedSource)}
        </Badge>
      </Group>
    </Stack>
  )
}
