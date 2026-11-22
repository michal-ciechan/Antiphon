import { Alert, Anchor, Badge, Box, Button, Group, Paper, Stack, Text, Tooltip } from '@mantine/core'
import { TbAlertTriangle, TbArrowLeft, TbExternalLink, TbGitBranch, TbRefresh } from 'react-icons/tb'
import { Link } from 'react-router'
import type { ReviewRunDetailDto } from '../../../api/reviewEngine'
import { BlastMini } from '../components/Blast'
import { CostText } from '../components/CostText'
import { OwnerChips } from '../components/OwnerChips'
import { PersonChip } from '../components/PersonChip'
import { ModelBadges, RunStatusBadge, StageText } from '../components/ReviewBadges'
import { RoundPips } from '../components/RoundPips'
import { fmtAgo, runCostSplit, shortSha } from '../reviewFormat'
import { WAITING_ON_META } from '../reviewMeta'
import { failedExecution } from './workflow/overlay'

/** Subject, status, round, blast radius, owners and cost; Refresh asks the code host for new events. */
export function RunHeader({ run, now, onRefresh, refreshing }: { run: ReviewRunDetailDto; now: number; onRefresh: () => void; refreshing: boolean }) {
  const s = run.subject
  const openedByOther = s.openedByLogin !== s.authorLogin
  const failed = run.status === 'Failed' ? failedExecution(run) : undefined
  return (
    <Stack gap={6} data-testid="review-run-header">
      <Anchor component={Link} to="/reviews" size="xs" c="dimmed">
        <Group gap={4}>
          <TbArrowLeft size={12} /> Review runs
        </Group>
      </Anchor>
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="md">
        <Box style={{ minWidth: 0, flex: 1 }}>
          <Group gap={8} wrap="wrap">
            <Text size="xs" ff="monospace" c="dimmed">
              {s.repo}
              {s.prNumber !== null && `#${s.prNumber}`}
            </Text>
            {s.url && (
              <Anchor href={s.url} target="_blank" rel="noreferrer" size="xs">
                <Group gap={2} wrap="nowrap">
                  {s.host} <TbExternalLink size={11} />
                </Group>
              </Anchor>
            )}
            <RunStatusBadge status={run.status} />
            <StageText stage={run.stage} />
            {run.waitingOn && (
              <Badge size="sm" variant="light" color={WAITING_ON_META[run.waitingOn].color}>
                waiting on {WAITING_ON_META[run.waitingOn].label.toLowerCase()}
              </Badge>
            )}
          </Group>
          <Text fw={700} size="xl" lh={1.25} mt={2}>
            {s.title}
          </Text>
          <Group gap={10} mt={4} wrap="wrap">
            <PersonChip login={s.authorLogin} displayName={s.authorDisplayName} size="xs" />
            {openedByOther ? (
              <Text size="xs" c="grape.3">
                opened by @{s.openedByLogin}
                {s.authorResolvedBy ? `, author resolved via ${s.authorResolvedBy}` : ', author resolved'}
              </Text>
            ) : (
              s.authorResolvedBy && (
                <Tooltip multiline w={320} label={`Resolved author: ${s.authorResolvedBy}`}>
                  <Badge size="xs" variant="light" color="grape" tt="none">
                    resolved author
                  </Badge>
                </Tooltip>
              )
            )}
            <Group gap={4} wrap="nowrap">
              <TbGitBranch size={12} color="var(--mantine-color-dimmed)" />
              <Text size="xs" ff="monospace" c="dimmed">
                {s.headRef} → {s.baseRef} @ {shortSha(s.headSha)}
              </Text>
            </Group>
            <Text size="xs" c="dimmed">
              started {fmtAgo(run.startedAt, now)} · updated {fmtAgo(run.updatedAt, now)}
            </Text>
          </Group>
        </Box>
        <Paper withBorder radius="md" p="xs">
          <Group gap="lg" wrap="wrap" align="flex-start">
            <Box data-testid="review-run-round">
              <Text size="10px" c="dimmed" tt="uppercase" fw={700} mb={2}>
                Round
              </Text>
              <RoundPips round={run.round} botOnly={run.botOnlyRounds} />
            </Box>
            <Box>
              <Text size="10px" c="dimmed" tt="uppercase" fw={700} mb={2}>
                Blast
              </Text>
              <BlastMini blast={run.blastRadius} height={22} />
            </Box>
            <Box>
              <Text size="10px" c="dimmed" tt="uppercase" fw={700} mb={2}>
                Cost
              </Text>
              <CostText usd={run.costUsd} split={runCostSplit(run)} size="md" />
            </Box>
            <Box maw={220}>
              <Text size="10px" c="dimmed" tt="uppercase" fw={700} mb={2}>
                Models
              </Text>
              <ModelBadges models={run.models} max={5} />
            </Box>
            <Button size="xs" variant="default" leftSection={<TbRefresh size={14} />} loading={refreshing} onClick={onRefresh} data-testid="review-run-refresh">
              Refresh
            </Button>
          </Group>
        </Paper>
      </Group>
      {run.status === 'Failed' && (
        <Alert color="danger" variant="light" p={8} icon={<TbAlertTriangle size={16} />} title={<Text size="xs" fw={700}>Run failed{failed ? ` at ${failed.nodeId} (it${failed.iteration})` : ''}</Text>}>
          <Text size="xs">{failed?.summary ?? 'No failed step reported a reason.'} Refresh rechecks the same head.</Text>
        </Alert>
      )}
      <Group gap={4} wrap="wrap">
        <Text size="xs" c="dimmed">
          Owners:
        </Text>
        <OwnerChips owners={s.owners} />
        {run.categories.map((c) => (
          <Badge key={c} size="xs" variant="light" color="gray" radius="sm" tt="none">
            {c}
          </Badge>
        ))}
      </Group>
    </Stack>
  )
}
