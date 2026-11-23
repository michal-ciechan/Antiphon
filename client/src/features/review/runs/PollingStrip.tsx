import { Button, Group, Paper, Text, Tooltip } from '@mantine/core'
import { TbBrandGithub, TbRefresh } from 'react-icons/tb'
import { fmtAgo } from '../reviewFormat'
import { usePollNow, useReviewPolling } from '../useReview'

function every(seconds: number): string {
  return seconds % 60 === 0 ? `${seconds / 60} min` : `${seconds}s`
}

/**
 * Code host polling at a glance (`GET /api/review-polling`): off or every N minutes, when the last
 * pass ran and whether it was ok, and "Check GitHub now" (one pass, then the runs are refetched).
 * Hidden until the polling state loads.
 */
export function PollingStrip({ now }: { now: number }) {
  const polling = useReviewPolling()
  const poll = usePollNow()
  const p = polling.data
  if (!p) return null
  const last = p.lastPass
  const failed = last?.repos.filter((r) => r.error) ?? []
  return (
    <Paper withBorder radius="md" px="sm" py={6} data-testid="review-polling-strip">
      <Group justify="space-between" gap="xs" wrap="wrap">
        <Group gap={8} wrap="wrap">
          <TbBrandGithub size={14} />
          <Text size="xs" fw={600}>
            {p.enabled ? `Polling every ${every(p.intervalSeconds)}` : 'Polling off'}
          </Text>
          <Text size="xs" c="dimmed">
            {p.repos.length === 1 ? p.repos[0].repo : `${p.repos.length} repos`}
          </Text>
          {last ? (
            <Tooltip
              multiline
              w={360}
              disabled={failed.length === 0}
              label={failed.map((r) => `${r.repo}: ${r.error}`).join(' · ')}
            >
              <Text size="xs" c={last.ok ? 'dimmed' : 'danger.4'} data-testid="review-polling-last">
                last checked {fmtAgo(last.finishedAt, now)} · {last.ok ? 'ok' : `failed for ${failed.length} of ${last.repos.length}`}
              </Text>
            </Tooltip>
          ) : (
            <Text size="xs" c="dimmed">
              not checked since the server started
            </Text>
          )}
        </Group>
        <Button
          size="compact-xs"
          variant="default"
          leftSection={<TbRefresh size={12} />}
          loading={poll.isPending || p.running}
          disabled={p.repos.length === 0}
          onClick={() => poll.mutate()}
          data-testid="review-polling-check-now"
        >
          Check GitHub now
        </Button>
      </Group>
    </Paper>
  )
}
