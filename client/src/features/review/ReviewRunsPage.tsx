import { Alert, Badge, Box, Chip, Group, Loader, SegmentedControl, Select, Stack, Text, TextInput, UnstyledButton } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useMemo } from 'react'
import { TbFilterOff, TbSearch, TbX } from 'react-icons/tb'
import { getApiErrorMessage } from '../../api/client'
import type { ReviewRunSummaryDto } from '../../api/reviewEngine'
import { CostText } from './components/CostText'
import { FakeApiBanner } from './components/FakeApiBanner'
import { useReviewLiveUpdates } from './reviewApi'
import { RUN_STATUS_META, modelShort } from './reviewMeta'
import { IN_REVIEW_STATUSES, STATUS_ORDER, anyClientFilter, applyRunFilters, hasAllStatuses, toggleStatuses } from './runs/runFilters'
import { RunsBoard } from './runs/RunsBoard'
import { RunsKpiStrip } from './runs/RunsKpiStrip'
import { RunsTable } from './runs/RunsTable'
import { useNow } from './useNow'
import { useReviewKpis, useReviewRuns } from './useReview'
import { useReviewUiStore, type RunsView } from './useReviewUiStore'

function ActiveFilters({ shown, total }: { shown: number; total: number }) {
  const filters = useReviewUiStore((s) => s.runsFilters)
  const patch = useReviewUiStore((s) => s.patchRunsFilters)
  const clear = useReviewUiStore((s) => s.clearRunsFilters)
  const chips: { label: string; color: string; remove: () => void }[] = []
  let rest = filters.statuses
  if (hasAllStatuses(filters, IN_REVIEW_STATUSES)) {
    chips.push({ label: 'In review', color: 'active', remove: () => patch({ statuses: toggleStatuses(filters, IN_REVIEW_STATUSES) }) })
    rest = rest.filter((s) => !IN_REVIEW_STATUSES.includes(s))
  }
  for (const s of rest) chips.push({ label: RUN_STATUS_META[s].label, color: RUN_STATUS_META[s].color, remove: () => patch({ statuses: filters.statuses.filter((x) => x !== s) }) })
  if (filters.humans) chips.push({ label: 'Waiting on humans', color: 'pink', remove: () => patch({ humans: false }) })
  if (filters.deepRounds) chips.push({ label: 'Round 3+', color: 'gray', remove: () => patch({ deepRounds: false }) })
  for (const m of filters.models) chips.push({ label: `uses ${modelShort(m)}`, color: 'grape', remove: () => patch({ models: filters.models.filter((x) => x !== m) }) })
  return (
    <Group gap={6} mih={22}>
      <Text size="xs" fw={chips.length > 0 ? 600 : 400} c={chips.length > 0 ? undefined : 'dimmed'}>
        Showing {shown} of {total} runs
      </Text>
      {chips.length > 0 && (
        <Text size="xs" c="dimmed">
          · filtered by
        </Text>
      )}
      {chips.map((c) => (
        <Badge
          key={c.label}
          size="sm"
          color={c.color}
          variant="light"
          rightSection={
            <UnstyledButton onClick={c.remove} aria-label={`Remove ${c.label} filter`} style={{ display: 'flex' }}>
              <TbX size={11} />
            </UnstyledButton>
          }
        >
          {c.label}
        </Badge>
      ))}
      {anyClientFilter(filters) && (
        <UnstyledButton onClick={clear}>
          <Group gap={4}>
            <TbFilterOff size={13} color="var(--mantine-color-gray-5)" />
            <Text size="xs" c="gray.5" td="underline">
              Clear all
            </Text>
          </Group>
        </UnstyledButton>
      )}
    </Group>
  )
}

/** /reviews — every review run: KPI filter cards, a dense table (default) or a board, search. */
export function ReviewRunsPage() {
  useReviewLiveUpdates()
  const now = useNow()
  const filters = useReviewUiStore((s) => s.runsFilters)
  const patch = useReviewUiStore((s) => s.patchRunsFilters)
  const view = useReviewUiStore((s) => s.runsView)
  const setView = useReviewUiStore((s) => s.setRunsView)
  const [q] = useDebouncedValue(filters.q, 250)

  const all = useReviewRuns({})
  const searched = useReviewRuns({ q: q.trim() || undefined, repo: filters.repo ?? undefined })
  const kpis = useReviewKpis()

  const allRuns: ReviewRunSummaryDto[] = useMemo(() => all.data?.items ?? [], [all.data])
  const base = useMemo(() => searched.data?.items ?? [], [searched.data])
  const rows = useMemo(() => applyRunFilters(base, filters), [base, filters])
  const repos = useMemo(() => [...new Set(allRuns.map((r) => r.subject.repo))].sort(), [allRuns])
  const counts = useMemo(() => {
    const c = new Map<string, number>()
    for (const r of base) c.set(r.status, (c.get(r.status) ?? 0) + 1)
    return c
  }, [base])

  const inReview = allRuns.filter((r) => IN_REVIEW_STATUSES.includes(r.status)).length
  const atGate = allRuns.filter((r) => r.status === 'AtGate').length
  const total = rows.reduce((sum, r) => sum + r.costUsd, 0)
  const error = all.error ?? searched.error

  return (
    <Stack gap="sm" p="md">
      <FakeApiBanner />
      <Group justify="space-between" align="flex-end" wrap="wrap">
        <Box>
          <Text fw={700} size="lg">
            Review runs
          </Text>
          <Text size="xs" c="dimmed">
            {allRuns.length} runs · {inReview} in review · {atGate} at a human gate · live
          </Text>
        </Box>
        <Group gap="xs" wrap="wrap">
          <TextInput
            size="xs"
            placeholder="Search PR, title, author, branch"
            aria-label="Search runs"
            leftSection={searched.isFetching && filters.q ? <Loader size={10} /> : <TbSearch size={12} />}
            value={filters.q}
            onChange={(e) => patch({ q: e.currentTarget.value })}
            w={240}
          />
          <Select
            size="xs"
            w={190}
            aria-label="Repository"
            placeholder="All repos"
            clearable
            data={repos}
            value={filters.repo}
            onChange={(repo) => patch({ repo })}
          />
          <SegmentedControl
            size="xs"
            value={view}
            onChange={(v) => setView(v as RunsView)}
            data={[
              { value: 'table', label: 'Table' },
              { value: 'board', label: 'Board' },
            ]}
          />
        </Group>
      </Group>

      {error && (
        <Alert color="danger" variant="light" title="Could not load review runs">
          {getApiErrorMessage(error, 'The review API did not answer.')}
        </Alert>
      )}

      <RunsKpiStrip kpis={kpis.data} runs={allRuns} />

      <Chip.Group multiple value={filters.statuses} onChange={(v) => patch({ statuses: v as typeof filters.statuses })}>
        <Group gap={4}>
          {STATUS_ORDER.map((s) => (
            <Chip key={s} value={s} size="xs" color={RUN_STATUS_META[s].color} variant="light">
              {RUN_STATUS_META[s].label} {counts.get(s) ?? 0}
            </Chip>
          ))}
        </Group>
      </Chip.Group>

      <ActiveFilters shown={rows.length} total={base.length} />

      {all.isLoading ? (
        <Group justify="center" py="xl">
          <Loader size="sm" />
        </Group>
      ) : view === 'table' ? (
        <RunsTable runs={rows} now={now} />
      ) : (
        <RunsBoard runs={rows} now={now} />
      )}

      <Group justify="space-between" px={4}>
        <Text size="xs" c="dimmed">
          Cost in dollars: metered API calls are actual; seat models are an API-equivalent estimate (not billed). Hover a cost for the exact amount.
        </Text>
        <Text size="xs" ff="monospace">
          Σ <CostText usd={total} /> over {rows.length} shown
        </Text>
      </Group>
    </Stack>
  )
}
