import { Box, Group, Paper, Skeleton, Text, Tooltip, UnstyledButton } from '@mantine/core'
import type { ReviewKpisDto, ReviewRunSummaryDto } from '../../../api/reviewEngine'
import { KpiCard } from '../components/KpiCard'
import { ModelBadge } from '../components/ReviewBadges'
import { fmtCost } from '../reviewFormat'
import { useReviewUiStore } from '../useReviewUiStore'
import { IN_REVIEW_STATUSES, hasAllStatuses, toggleStatuses } from './runFilters'

/** The KPI cards. "In review", "Waiting on humans", "Avg rounds" and each model row are toggle filters. */
export function RunsKpiStrip({ kpis, runs }: { kpis: ReviewKpisDto | undefined; runs: ReviewRunSummaryDto[] }) {
  const filters = useReviewUiStore((s) => s.runsFilters)
  const patch = useReviewUiStore((s) => s.patchRunsFilters)
  if (!kpis) return <Skeleton h={96} radius="md" />
  const deep = runs.filter((r) => r.round >= 3).length
  const maxCost = Math.max(...kpis.modelUsage.map((u) => u.costUsd), 0.0001)
  return (
    <Box style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr)) ', gap: 8 }}>
      <KpiCard
        label="In review"
        value={kpis.inReview}
        accent="active"
        sub="queued or running"
        selected={hasAllStatuses(filters, IN_REVIEW_STATUSES)}
        onToggle={() => patch({ statuses: toggleStatuses(filters, IN_REVIEW_STATUSES) })}
        hint="queued and running runs"
      />
      <KpiCard
        label="Waiting on humans"
        value={kpis.waitingOnHumans}
        accent="pink"
        sub="parked on a gate, no seat held"
        selected={filters.humans}
        onToggle={() => patch({ humans: !filters.humans })}
        hint="runs waiting on a human"
      />
      <KpiCard
        label="Avg rounds"
        value={kpis.averageRounds.toFixed(1)}
        accent="gray"
        sub={`to approval · ${deep} run${deep === 1 ? '' : 's'} at round 3+`}
        selected={filters.deepRounds}
        onToggle={() => patch({ deepRounds: !filters.deepRounds })}
        hint="runs at round 3 or later"
      />
      <KpiCard label="Cost today" value={<Text span fw={700} size="xl" ff="monospace">{fmtCost(kpis.costTodayUsd)}</Text>} accent="cyan" sub="actual API + seat estimate" />
      <KpiCard
        label="Per accepted finding"
        value={
          <Text span fw={700} size="xl" ff="monospace">
            {kpis.costPerAcceptedFindingUsd === null ? '—' : fmtCost(kpis.costPerAcceptedFindingUsd)}
          </Text>
        }
        accent="success"
        sub="cost ÷ findings fixed or accepted"
      />
      <Paper withBorder p="xs" radius="md" style={{ gridColumn: 'span 2', minWidth: 300, borderTopWidth: 2, borderTopColor: 'var(--mantine-color-grape-6)' }}>
        <Text size="xs" c="dimmed" tt="uppercase" fw={600} style={{ letterSpacing: 0.5 }} mb={4}>
          Models in use · click to filter
        </Text>
        {kpis.modelUsage.length === 0 && (
          <Text size="xs" c="dimmed">
            No model calls yet.
          </Text>
        )}
        {kpis.modelUsage.slice(0, 6).map((u) => {
          const on = filters.models.includes(u.model)
          return (
            <Tooltip key={u.model} label={`${on ? 'Filtering: ' : 'Filter: '}runs that used ${u.model}`} openDelay={300}>
              <UnstyledButton
                w="100%"
                aria-pressed={on}
                onClick={() => patch({ models: on ? filters.models.filter((m) => m !== u.model) : [...filters.models, u.model] })}
                style={{ borderRadius: 4, background: on ? 'var(--mantine-color-grape-light)' : undefined }}
                px={4}
              >
                <Group gap={6} wrap="nowrap">
                  <Box w={70}>
                    <ModelBadge model={u.model} usage={u} />
                  </Box>
                  <Box style={{ flex: 1 }} h={6} bg="dark.5">
                    <Box h={6} w={`${Math.max(2, (u.costUsd / maxCost) * 100)}%`} bg={u.transport === 'Http' ? 'cyan.6' : 'grape.6'} />
                  </Box>
                  <Text size="xs" ff="monospace" w={56} ta="right">
                    {u.calls} calls
                  </Text>
                  <Text size="xs" ff="monospace" w={44} ta="right">
                    {u.transport === 'Http' ? '' : '≈'}
                    {fmtCost(u.costUsd)}
                  </Text>
                </Group>
              </UnstyledButton>
            </Tooltip>
          )
        })}
      </Paper>
    </Box>
  )
}
