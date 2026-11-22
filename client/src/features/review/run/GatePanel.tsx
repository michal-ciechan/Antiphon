import { Alert, Badge, Box, Button, Group, Paper, Radio, Stack, Text, Textarea, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbCircleCheck, TbLock } from 'react-icons/tb'
import type { GateDecision, ReviewFindingDto, ReviewGateDto, ReviewRunDetailDto, ReviewViewerDto } from '../../../api/reviewEngine'
import { GatePasses } from '../components/GatePasses'
import { PersonChip } from '../components/PersonChip'
import { GateKindBadge, QueueStatusBadge } from '../components/ReviewBadges'
import { SectionLabel } from '../components/SectionLabel'
import { fmtAgo, personLabel, shortSha } from '../reviewFormat'
import { useGateDecision } from '../useReview'
import { decisionCopy, gateDecisions, viewerBlockReason } from './gateRules'

/** A decided gate: what was decided, by whom (`decidedBy`), when, and the steer. */
function DecidedGate({ gate, now }: { gate: ReviewGateDto; now: number }) {
  const copy = decisionCopy(gate.kind)
  return (
    <Stack gap={4}>
      <Group gap={6} wrap="wrap">
        <TbCircleCheck size={14} color="var(--mantine-color-success-5)" />
        <Text size="xs" fw={700}>
          {gate.decision ? (copy[gate.decision]?.label ?? gate.decision) : 'Decided'}
        </Text>
        <Text size="xs" c="dimmed">
          by
        </Text>
        {gate.decidedBy ? (
          <PersonChip login={gate.decidedBy} displayName={gate.decidedBy === gate.assigneeLogin ? gate.assigneeDisplayName : null} size="xs" />
        ) : (
          <Text size="xs" c="dimmed">
            unknown
          </Text>
        )}
        {gate.decidedAt && (
          <Tooltip label={new Date(gate.decidedAt).toLocaleString()}>
            <Text size="xs" c="dimmed">
              {fmtAgo(gate.decidedAt, now)}
            </Text>
          </Tooltip>
        )}
      </Group>
      {gate.steerPrompt && (
        <Text size="xs" c="gray.4" fs="italic">
          Steer: "{gate.steerPrompt}"
        </Text>
      )}
    </Stack>
  )
}

/**
 * Approve / Another round (with a steer) / Close, limited to the gate's `allowedDecisions`, or why
 * the viewer can't decide (`viewerCannotDecideReason`). A decided gate shows who decided it.
 */
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
  viewer: ReviewViewerDto
  now: number
}) {
  const [decision, setDecision] = useState<GateDecision | null>(null)
  const [steer, setSteer] = useState('')
  const decide = useGateDecision(run.id)
  const done = gate.status === 'Done'
  const blocked = viewerBlockReason(gate)
  const copy = decisionCopy(gate.kind)
  const decisions = gateDecisions(gate)
  const notOffered = (['Approve', 'AnotherRound', 'Close'] as const).filter((d) => !decisions.includes(d))
  const openBlockers = findings.filter((f) => f.severity === 'Blocker' && (f.status === 'Open' || f.status === 'Disputed' || f.status === 'WillFix'))
  const dispute = gate.kind === 'DisputedFindings'
  const viewerName = personLabel(viewer.login, viewer.displayName)

  const submit = () => {
    if (!decision) return
    decide.mutate(
      { gateId: gate.id, request: { decision, ...(decision === 'AnotherRound' && steer.trim() ? { steerPrompt: steer.trim() } : {}) } },
      {
        onSuccess: () => {
          notifications.show({ color: 'success', title: `${copy[decision].label}: recorded`, message: `Gate on ${run.subject.repo}#${run.subject.prNumber ?? ''} decided as ${viewerName}.` })
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
    <Paper withBorder p="xs" radius="md" style={{ borderTop: `2px solid var(--mantine-color-${done ? 'success' : 'pink'}-6)` }}>
      <SectionLabel right={<GateKindBadge kind={gate.kind} />}>{done ? 'Last gate decision' : 'Gate decision'}</SectionLabel>
      <Group gap={4} mb={4} wrap="wrap">
        <Text size="xs" c="dimmed">
          Opened {fmtAgo(gate.openedAt, now)} · {gate.teamKey ?? 'no team'} ·
        </Text>
        <PersonChip login={gate.assigneeLogin} displayName={gate.assigneeDisplayName} size="xs" />
        <QueueStatusBadge status={gate.status} />
      </Group>
      <Box mb={4}>
        <GatePasses passes={gate.passes} now={now} note={!done} />
      </Box>
      <Stack gap={2} mb={6}>
        {gate.reasons.map((r) => (
          <Text key={r} size="xs" c="pink.3">
            • {r}
          </Text>
        ))}
      </Stack>
      {done ? (
        <DecidedGate gate={gate} now={now} />
      ) : (
        <>
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
              {decisions.map((d) => (
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
          {notOffered.length > 0 && (
            <Text size="10px" c="dimmed" mt={4}>
              Not offered on this gate: {notOffered.map((d) => copy[d].label.toLowerCase()).join(', ')}.
            </Text>
          )}
          {decision === 'AnotherRound' && !blocked && !dispute && (
            <Textarea
              mt={6}
              size="xs"
              autosize
              minRows={2}
              label={<Text size="xs">Steer prompt (optional; shared prefix "Human steer from {viewerName}: …")</Text>}
              placeholder="e.g. Recheck F3 against the transaction isolation in UnitOfWorkMiddleware; skip style nits this round."
              value={steer}
              onChange={(e) => setSteer(e.currentTarget.value)}
            />
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
        </>
      )}
      {run.needsHumanApproval && !dispute && (
        <Badge mt={6} size="xs" variant="outline" color="pink" tt="none">
          human approval is sticky for the rest of this PR
        </Badge>
      )}
    </Paper>
  )
}
