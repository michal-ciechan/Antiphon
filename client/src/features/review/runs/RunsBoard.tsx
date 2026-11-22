import { Badge, Box, Group, Paper, ScrollArea, Stack, Text, UnstyledButton } from '@mantine/core'
import { useNavigate } from 'react-router'
import type { ReviewRunSummaryDto } from '../../../api/reviewEngine'
import { CostText } from '../components/CostText'
import { ModelBadges, StageText } from '../components/ReviewBadges'
import { RoundPips } from '../components/RoundPips'
import { fmtAgo, prRef } from '../reviewFormat'
import { RUN_STATUS_META } from '../reviewMeta'
import { AuthorLine, OpenFindingsCell } from './RunsTable'
import { BOARD_COLUMNS } from './runFilters'

function BoardCard({ run, now }: { run: ReviewRunSummaryDto; now: number }) {
  const navigate = useNavigate()
  return (
    <UnstyledButton onClick={() => navigate(`/reviews/${run.id}`)} aria-label={`Open ${prRef(run.subject)}`}>
      <Paper withBorder radius="sm" p={8} style={{ opacity: run.status === 'Merged' || run.status === 'Closed' ? 0.6 : 1 }}>
        <Group justify="space-between" gap={4} wrap="nowrap">
          <Text size="xs" ff="monospace" c="dimmed">
            {prRef(run.subject)}
          </Text>
          <RoundPips round={run.round} botOnly={run.botOnlyRounds} compact />
        </Group>
        <Text size="sm" fw={500} lineClamp={2} mt={2} lh={1.3}>
          {run.subject.title}
        </Text>
        <Box mt={4}>
          <AuthorLine run={run} />
        </Box>
        <Group gap={6} mt={4} wrap="nowrap">
          <StageText stage={run.stage} />
          {run.status === 'Failed' && (
            <Badge size="xs" color="danger" variant="light">
              failed
            </Badge>
          )}
        </Group>
        <Box mt={4}>
          <OpenFindingsCell run={run} />
        </Box>
        <Group justify="space-between" mt={6} wrap="nowrap" gap={4}>
          <ModelBadges models={run.models} max={3} />
          <CostText usd={run.costUsd} />
        </Group>
        <Text size="xs" c="dimmed" mt={2}>
          {fmtAgo(run.updatedAt, now)}
        </Text>
      </Paper>
    </UnstyledButton>
  )
}

/** One column per "whose move" group; header shows count and Σ cost. */
export function RunsBoard({ runs, now }: { runs: ReviewRunSummaryDto[]; now: number }) {
  return (
    <ScrollArea type="auto" offsetScrollbars>
      <Box style={{ display: 'grid', gridTemplateColumns: `repeat(${BOARD_COLUMNS.length}, minmax(220px, 1fr))`, gap: 8, minWidth: BOARD_COLUMNS.length * 228 }}>
        {BOARD_COLUMNS.map((col) => {
          const items = runs.filter((r) => col.statuses.includes(r.status))
          const cost = items.reduce((sum, r) => sum + r.costUsd, 0)
          return (
            <Stack key={col.key} gap={6}>
              <Paper p={6} radius="sm" bg="dark.6" style={{ borderTop: `3px solid var(--mantine-color-${col.color}-6)` }}>
                <Group justify="space-between" wrap="nowrap">
                  <Text size="sm" fw={700}>
                    {col.title}
                  </Text>
                  <Badge size="sm" variant="filled" color={col.color}>
                    {items.length}
                  </Badge>
                </Group>
                <Group gap={4}>
                  <CostText usd={cost} dimmed />
                  <Text size="xs" c="dimmed">
                    · {col.statuses.map((s) => RUN_STATUS_META[s].label.toLowerCase()).join(', ')}
                  </Text>
                </Group>
              </Paper>
              {items.map((r) => (
                <BoardCard key={r.id} run={r} now={now} />
              ))}
              {items.length === 0 && (
                <Text size="xs" c="dimmed" ta="center" py="sm">
                  —
                </Text>
              )}
            </Stack>
          )
        })}
      </Box>
    </ScrollArea>
  )
}
