import { Alert, Badge, Box, Button, Group, Paper, Radio, Stack, Text, Textarea, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbLock } from 'react-icons/tb'
import { getApiErrorMessage } from '../../../api/client'
import type { GateDecision, ReviewFindingDto, ReviewGateDto, ReviewRunDetailDto } from '../../../api/reviewEngine'
import { PersonChip } from '../components/PersonChip'
import { GateKindBadge, QueueStatusBadge } from '../components/ReviewBadges'
import { SectionLabel } from '../components/SectionLabel'
import { fmtAgo, shortSha } from '../reviewFormat'
import { useGateDecision } from '../useReview'
import type { ReviewViewer } from '../useReviewViewerStore'
import { decisionCopy, gateBlockReason } from './gateRules'

const DECISIONS: GateDecision[] = ['Approve', 'AnotherRound', 'Close']

/** Approve / Another round (with a steer) / Close, or why the viewer can't decide. */
export function GatePanel({
  run,
  gate,
  findings,
  viewer,
  now,
}: {
  run: ReviewRunDetailDto
  gate: ReviewGateDto
  findings: ReviewFindingDto[]
  viewer: ReviewViewer
  now: number
}) {
  const [decision, setDecision] = useState<GateDecision | null>(null)
  const [steer, setSteer] = useState('')
  const decide = useGateDecision(run.id)
  const blocked = gateBlockReason(run, gate, viewer)
  const copy = decisionCopy(gate.kind)
  const openBlockers = findings.filter((f) => f.severity === 'Blocker' && (f.status === 'Open' || f.status === 'Disputed' || f.status === 'WillFix'))
  const dispute = gate.kind === 'DisputedFindings'

  const submit = () => {
    if (!decision) return
    decide.mutate(
      { gateId: gate.id, request: { decision, ...(decision === 'AnotherRound' && steer.trim() ? { steerPrompt: steer.trim() } : {}) } },
      {
        onSuccess: () => {
          notifications.show({ color: 'success', title: `${copy[decision].label}: recorded`, message: `Gate on ${run.subject.repo}#${run.subject.prNumber ?? ''} decided as ${viewer.displayName}.` })
          setDecision(null)
          setSteer('')
        },
      },
    )
  }

  const buttonLabel =
    decision === 'Approve' && !dispute
      ? `Approve ${shortSha(run.subject.headSha)}`
      : decision === 'AnotherRound' && !dispute
        ? `Start round ${run.round + 1}${steer.trim() ? ' with steer' : ''}`
        : decision
          ? copy[decision].label
          : 'Pick a decision'

  return (
    <Paper withBorder p="xs" radius="md" style={{ borderTop: '2px solid var(--mantine-color-pink-6)' }}>
      <SectionLabel right={<GateKindBadge kind={gate.kind} />}>Gate decision</SectionLabel>
      <Group gap={4} mb={4} wrap="wrap">
        <Text size="xs" c="dimmed">
          Opened {fmtAgo(gate.openedAt, now)} · {gate.teamKey ?? 'no team'} ·
        </Text>
        <PersonChip login={gate.assigneeLogin} displayName={gate.assigneeLogin === viewer.login ? viewer.displayName : null} size="xs" />
        <QueueStatusBadge status={gate.status} />
      </Group>
      {gate.passedBy.length > 0 && (
        <Text size="xs" c="warning.4" mb={4}>
          Passed by {gate.passedBy.map((p) => `@${p}`).join(', ')} (excluded from the next pick)
        </Text>
      )}
      <Stack gap={2} mb={6}>
        {gate.reasons.map((r) => (
          <Text key={r} size="xs" c="pink.3">
            • {r}
          </Text>
        ))}
      </Stack>
      {!dispute &&
        (openBlockers.length > 0 ? (
          <Alert color="danger" variant="light" p={6} mb={6} icon={<TbLock size={14} />} title={<Text size="xs" fw={700}>Bot approval blocked: {openBlockers.length} open blocker{openBlockers.length === 1 ? '' : 's'}</Text>}>
            <Text size="xs">
              {openBlockers.map((f) => f.key).join(', ')} · the bot never approves while a blocker is open. A human approve here records it as accepted risk.
            </Text>
          </Alert>
        ) : (
          <Text size="xs" c="success.4" mb={6}>
            No open blockers.
          </Text>
        ))}
      <Radio.Group value={decision ?? ''} onChange={(v) => setDecision(v as GateDecision)}>
        <Stack gap={6}>
          {DECISIONS.map((d) => (
            <Radio
              key={d}
              size="xs"
              value={d}
              disabled={!!blocked}
              label={copy[d].label}
              description={
                d === 'Approve' && !dispute && openBlockers.length > 0
                  ? `${copy[d].description} ${openBlockers.length} open blocker${openBlockers.length === 1 ? '' : 's'} recorded as accepted risk by you.`
                  : copy[d].description
              }
            />
          ))}
        </Stack>
      </Radio.Group>
      {decision === 'AnotherRound' && !blocked && !dispute && (
        <Textarea
          mt={6}
          size="xs"
          autosize
          minRows={2}
          label={<Text size="xs">Steer prompt (optional; shared prefix "Human steer from {viewer.displayName}: …")</Text>}
          placeholder="e.g. Recheck F3 against the transaction isolation in UnitOfWorkMiddleware; skip style nits this round."
          value={steer}
          onChange={(e) => setSteer(e.currentTarget.value)}
        />
      )}
      {decide.error && (
        <Alert color="danger" variant="light" p={6} mt={6}>
          <Text size="xs">{getApiErrorMessage(decide.error, 'The decision was not recorded.')}</Text>
        </Alert>
      )}
      {blocked ? (
        <Box mt={8}>
          <Tooltip multiline w={300} label={blocked}>
            <Box>
              <Button size="xs" fullWidth disabled leftSection={<TbLock size={12} />}>
                Decide gate
              </Button>
            </Box>
          </Tooltip>
          <Text size="xs" c="dimmed" mt={4} fs="italic">
            {blocked}
          </Text>
        </Box>
      ) : (
        <Button
          mt={8}
          size="xs"
          fullWidth
          color={decision === 'Approve' ? 'success' : decision === 'Close' ? 'gray' : 'pink'}
          disabled={!decision}
          loading={decide.isPending}
          onClick={submit}
        >
          {buttonLabel}
        </Button>
      )}
      {run.needsHumanApproval && !dispute && (
        <Badge mt={6} size="xs" variant="outline" color="pink" tt="none">
          human approval is sticky for the rest of this PR
        </Badge>
      )}
    </Paper>
  )
}
