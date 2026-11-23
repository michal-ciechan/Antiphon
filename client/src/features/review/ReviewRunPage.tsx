import { Anchor, Badge, Button, Group, Paper, Skeleton, Stack, Tabs, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbArrowLeft, TbGitPullRequest, TbHierarchy3, TbTimeline } from 'react-icons/tb'
import { Link, useParams, useSearchParams } from 'react-router'
import { ApiError } from '../../api/client'
import { FakeApiBanner } from './components/FakeApiBanner'
import { GateKindBadge } from './components/ReviewBadges'
import { QueryErrorAlert } from './components/ReviewProblem'
import { useReviewLiveUpdates } from './reviewApi'
import { FindingsTab } from './run/findings/FindingsTab'
import { openGate, viewerBlockReason } from './run/gateRules'
import { RequestRoundModal } from './run/RequestRoundModal'
import { RunHeader } from './run/RunHeader'
import { TimelineTab } from './run/TimelineTab'
import { WorkflowTab } from './run/workflow/WorkflowTab'
import { useNow } from './useNow'
import { personLabel } from './reviewFormat'
import { useRefreshReviewRun, useReviewFindings, useReviewMe, useReviewRun } from './useReview'

const TABS = ['findings', 'workflow', 'timeline'] as const
type Tab = (typeof TABS)[number]

const PULSE_CSS = '@keyframes reviewPulse { 0%, 100% { box-shadow: 0 0 0 0 rgba(59,123,245,0.55); } 50% { box-shadow: 0 0 0 5px rgba(59,123,245,0); } }'

function RunSkeleton() {
  return (
    <Stack gap="sm" p="md">
      <Skeleton h={12} w={110} />
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={8} style={{ flex: 1, minWidth: 260 }}>
          <Skeleton h={14} w={220} />
          <Skeleton h={26} w="70%" />
          <Skeleton h={12} w="50%" />
        </Stack>
        <Skeleton h={64} w={420} radius="md" />
      </Group>
      <Skeleton h={34} radius="sm" />
      <Skeleton h={360} radius="md" />
    </Stack>
  )
}

/**
 * /reviews/:runId — one run: header, gate, and the Findings / Workflow / Timeline tabs (?tab=). The
 * viewer is `GET /api/review-me`; what they may decide comes from each gate's `viewerCanDecide`.
 */
export function ReviewRunPage() {
  useReviewLiveUpdates()
  const { runId } = useParams<{ runId: string }>()
  const [params, setParams] = useSearchParams()
  const tab: Tab = TABS.find((t) => t === params.get('tab')) ?? 'findings'
  const now = useNow()
  const me = useReviewMe()
  const run = useReviewRun(runId)
  const findings = useReviewFindings(runId)
  const refresh = useRefreshReviewRun(runId ?? '')
  const [roundOpen, setRoundOpen] = useState(false)
  const requestRefresh = () =>
    refresh.mutate(undefined, {
      onSuccess: () => notifications.show({ color: 'gray', message: 'Refresh requested: Antiphon re-reads the change from the code host.' }),
    })
  const setTab = (t: string | null) =>
    setParams(
      (p) => {
        if (!t || t === 'findings') p.delete('tab')
        else p.set('tab', t)
        return p
      },
      { replace: true },
    )

  if (run.isLoading || me.isLoading) return <RunSkeleton />
  if (!run.data) {
    const missing = run.error instanceof ApiError && run.error.status === 404
    return (
      <Stack gap="sm" p="md">
        <FakeApiBanner />
        <Anchor component={Link} to="/reviews" size="xs" c="dimmed">
          <Group gap={4}>
            <TbArrowLeft size={12} /> Review runs
          </Group>
        </Anchor>
        <QueryErrorAlert
          title={missing ? 'No such review run' : 'Could not load the run'}
          error={run.error}
          onRetry={missing ? undefined : () => void run.refetch()}
          retrying={run.isFetching}
        />
      </Stack>
    )
  }

  const r = run.data
  const gate = openGate(r)
  const items = findings.data?.items ?? []
  const open = items.filter((f) => f.status === 'Open' || f.status === 'Disputed' || f.status === 'WillFix').length

  return (
    <Stack gap="sm" p="md">
      <FakeApiBanner />
      {run.error && <QueryErrorAlert title="Could not refresh the run; showing the last copy" error={run.error} onRetry={() => void run.refetch()} retrying={run.isFetching} />}
      <RunHeader run={r} now={now} onRefresh={requestRefresh} refreshing={refresh.isPending} onRequestRound={() => setRoundOpen(true)} />
      <RequestRoundModal runId={r.id} opened={roundOpen} onClose={() => setRoundOpen(false)} />
      {gate && tab !== 'findings' && (
        <Paper withBorder p={6} radius="md" style={{ borderLeft: '3px solid var(--mantine-color-pink-6)' }}>
          <Group justify="space-between" wrap="wrap" gap="xs">
            <Group gap={6} wrap="wrap">
              <GateKindBadge kind={gate.kind} />
              <Text size="xs">
                open · {gate.assigneeLogin ? `assigned to ${personLabel(gate.assigneeLogin, gate.assigneeDisplayName)}` : `unassigned in ${gate.teamKey ?? 'the team queue'}`}
              </Text>
              <Text size="xs" c="dimmed">
                {viewerBlockReason(gate) ?? 'You can decide it.'}
              </Text>
            </Group>
            <Button size="compact-xs" variant="light" color="pink" onClick={() => setTab('findings')}>
              Open the gate panel
            </Button>
          </Group>
        </Paper>
      )}
      <Tabs value={tab} onChange={setTab} keepMounted>
        <Tabs.List>
          <Tabs.Tab value="findings" leftSection={<TbGitPullRequest size={14} />} rightSection={<Badge size="xs" variant="light" color={r.openBlockers > 0 ? 'danger' : 'gray'}>{open}</Badge>}>
            Findings
          </Tabs.Tab>
          <Tabs.Tab value="workflow" leftSection={<TbHierarchy3 size={14} />}>
            Workflow
          </Tabs.Tab>
          <Tabs.Tab value="timeline" leftSection={<TbTimeline size={14} />}>
            Timeline
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="findings" pt="sm">
          {findings.error && !findings.data ? (
            <QueryErrorAlert title="Could not load findings" error={findings.error} onRetry={() => void findings.refetch()} retrying={findings.isFetching} />
          ) : !me.data ? (
            <QueryErrorAlert
              title="Could not tell who you are"
              error={me.error}
              fallback="GET /api/review-me did not answer, so replies and gate decisions are unavailable."
              onRetry={() => void me.refetch()}
              retrying={me.isFetching}
            />
          ) : findings.isLoading ? (
            <Stack gap={6}>
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} h={56} radius="sm" />
              ))}
            </Stack>
          ) : (
            <FindingsTab key={r.id} run={r} findings={items} viewer={me.data} now={now} />
          )}
        </Tabs.Panel>
        <Tabs.Panel value="workflow" pt="sm">
          {/* rendered only while visible: React Flow measures its container */}
          {tab === 'workflow' && <WorkflowTab run={r} now={now} />}
        </Tabs.Panel>
        <Tabs.Panel value="timeline" pt="sm">
          {tab === 'timeline' && <TimelineTab run={r} now={now} />}
        </Tabs.Panel>
      </Tabs>
      <style>{PULSE_CSS}</style>
    </Stack>
  )
}
