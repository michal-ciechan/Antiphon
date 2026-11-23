import { Badge, Box, Button, Chip, Group, Loader, Paper, SegmentedControl, Select, Skeleton, Stack, Text, TextInput, UnstyledButton } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useMemo, useState } from 'react'
import { TbDatabaseImport, TbFilterOff, TbPlus, TbSearch, TbX } from 'react-icons/tb'
import type { ReviewRunSummaryDto } from '../../api/reviewEngine'
import { CostText } from './components/CostText'
import { FakeApiBanner, FakeDataSwitch } from './components/FakeApiBanner'
import { QueryErrorAlert } from './components/ReviewProblem'
import { useReviewLiveUpdates } from './reviewApi'
import { RUN_STATUS_META, modelShort } from './reviewMeta'
import { IN_REVIEW_STATUSES, STATUS_ORDER, anyClientFilter, applyRunFilters, hasAllStatuses, toggleStatuses } from './runs/runFilters'
import { NewReviewModal } from './runs/NewReviewModal'
import { PollingStrip } from './runs/PollingStrip'
import { RunsBoard } from './runs/RunsBoard'
import { RunsKpiStrip } from './runs/RunsKpiStrip'
import { RunsTable } from './runs/RunsTable'
import { useNow } from './useNow'
import { useReviewKpis, useReviewRuns, useSeedReviewDemo } from './useReview'
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

/** No runs at all: start one, or (when the server seeds demo data) load the demo runs. */
function NoReviewsYet({ onNew }: { onNew: () => void }) {
  const seed = useSeedReviewDemo()
  const demoUnavailable = useReviewUiStore((s) => s.demoSeedUnavailable)
  return (
    <Paper withBorder radius="md" p="xl" data-testid="review-runs-empty">
      <Stack align="center" gap="xs">
        <Text fw={700} size="lg">
          No reviews yet
        </Text>
        <Text size="sm" c="dimmed" ta="center" maw={460}>
          Start a review of a GitHub pull request or a local commit range. Runs appear here as Antiphon works through them.
        </Text>
        <Group gap="xs" mt="xs">
          <Button size="xs" leftSection={<TbPlus size={14} />} onClick={onNew} data-testid="review-runs-empty-new">
            New review
          </Button>
          {!demoUnavailable && (
            <Button size="xs" variant="default" leftSection={<TbDatabaseImport size={14} />} loading={seed.isPending} onClick={() => seed.mutate()} data-testid="review-runs-load-demo">
              Load demo data
            </Button>
          )}
        </Group>
      </Stack>
    </Paper>
  )
}

function RunsSkeleton() {
  return (
    <Stack gap={6}>
      {[0, 1, 2, 3, 4].map((i) => (
        <Skeleton key={i} h={44} radius="sm" />
      ))}
    </Stack>
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
  const [intakeOpen, setIntakeOpen] = useState(false)

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
  const error = all.error ?? searched.error
  const empty = !!all.data && allRuns.length === 0
  const retry = () => {
    void all.refetch()
    void searched.refetch()
    void kpis.refetch()
  }

  return (
    <Stack gap="sm" p="md">
      <FakeApiBanner />
      <NewReviewModal opened={intakeOpen} onClose={() => setIntakeOpen(false)} />
      <Group justify="space-between" align="flex-end" wrap="wrap">
        <Box>
          <Text fw={700} size="lg">
            Review runs
          </Text>
          {all.data ? (
            <Text size="xs" c="dimmed">
              {allRuns.length} runs · {inReview} in review · {atGate} at a human gate · live
            </Text>
          ) : (
            all.isLoading && <Skeleton h={12} w={260} mt={4} />
          )}
        </Box>
        <Group gap="xs" wrap="wrap">
          <FakeDataSwitch />
          <Button size="xs" leftSection={<TbPlus size={14} />} onClick={() => setIntakeOpen(true)} data-testid="review-runs-new">
            New review
          </Button>
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

      <PollingStrip now={now} />

      {error && <QueryErrorAlert title="Could not load review runs" error={error} onRetry={retry} retrying={all.isFetching || searched.isFetching} />}

      {all.isLoading ? (
        <>
          <Skeleton h={96} radius="md" />
          <RunsSkeleton />
        </>
      ) : empty ? (
        <NoReviewsYet onNew={() => setIntakeOpen(true)} />
      ) : all.data ? (
        <RunsList kpis={kpis} runs={allRuns} rows={rows} base={base} counts={counts} view={view} now={now} />
      ) : null}
    </Stack>
  )
}

/** The KPI strip, status chips, active filters, the table or board, and the cost footer. */
function RunsList({
  kpis,
  runs,
  rows,
  base,
  counts,
  view,
  now,
}: {
  kpis: ReturnType<typeof useReviewKpis>
  runs: ReviewRunSummaryDto[]
  rows: ReviewRunSummaryDto[]
  base: ReviewRunSummaryDto[]
  counts: Map<string, number>
  view: RunsView
  now: number
}) {
  const filters = useReviewUiStore((s) => s.runsFilters)
  const patch = useReviewUiStore((s) => s.patchRunsFilters)
  const total = rows.reduce((sum, r) => sum + r.costUsd, 0)
  const totalSplit = { actual: rows.reduce((sum, r) => sum + r.costActualUsd, 0), estimate: rows.reduce((sum, r) => sum + r.costEstimatedUsd, 0) }
  return (
    <>
      {kpis.error && !kpis.data ? (
        <QueryErrorAlert title="Could not load the KPIs" error={kpis.error} onRetry={() => void kpis.refetch()} retrying={kpis.isFetching} />
      ) : (
        <RunsKpiStrip kpis={kpis.data} runs={runs} />
      )}

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

      {view === 'table' ? <RunsTable runs={rows} now={now} /> : <RunsBoard runs={rows} now={now} />}

      <Group justify="space-between" px={4}>
        <Text size="xs" c="dimmed">
          Cost in dollars: metered API calls are actual; seat models are an API-equivalent estimate (not billed). Hover a cost for the exact amount.
        </Text>
        <Text size="xs" ff="monospace">
          Σ <CostText usd={total} split={totalSplit} /> over {rows.length} shown
        </Text>
      </Group>
    </>
  )
}
