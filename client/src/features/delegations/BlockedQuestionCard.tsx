import {
  Alert,
  Anchor,
  Box,
  Button,
  Collapse,
  Group,
  Paper,
  ScrollArea,
  Stack,
  Text,
  Textarea,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { TbSend } from 'react-icons/tb'
import { Link } from 'react-router'
import {
  agentTaskKeys,
  useCancelAgentTask,
  useEscalateAgentTask,
  useReplyToAgentTask,
  useRetryAgentTask,
  type AgentTaskDetailDto,
  type BlockedContextDto,
} from '../../api/agentTasks'
import { attentionKeys } from '../../api/attention'
import { getApiErrorMessage } from '../../api/client'
import { RenderedMarkdown } from '../../shared/RenderedMarkdown'
import { formatCost, shortId } from './taskVisuals'

/**
 * CARD-0033: the question, the box, then the context — one component for the drawer (`full`) and
 * the attention / thread reply rows (`compact`). Reading order is the product: question first so
 * the operator does not scroll a report to find what they are being asked.
 */
export function BlockedQuestionCard({
  detail,
  variant,
  autoFocus,
  onAnswered,
}: {
  detail: AgentTaskDetailDto
  variant: 'full' | 'compact'
  autoFocus?: boolean
  onAnswered?: () => void
}) {
  const blocked = detail.blocked
  if (!blocked) return null

  return (
    <Stack gap="sm" data-testid="blocked-question-card">
      {variant === 'full' ? (
        <FullCard detail={detail} blocked={blocked} autoFocus={autoFocus} onAnswered={onAnswered} />
      ) : (
        <CompactCard detail={detail} blocked={blocked} autoFocus={autoFocus} onAnswered={onAnswered} />
      )}
    </Stack>
  )
}

function FullCard({
  detail,
  blocked,
  autoFocus,
  onAnswered,
}: {
  detail: AgentTaskDetailDto
  blocked: BlockedContextDto
  autoFocus?: boolean
  onAnswered?: () => void
}) {
  const { summary } = detail
  const since = formatClock(blocked.blockedAt)
  const caption = [
    'Blocked',
    `round ${blocked.round}`,
    since ? `since ${since}` : null,
    `${formatCost(summary.subtreeCostUsd)} so far`,
  ]
    .filter(Boolean)
    .join(' · ')

  return (
    <Stack gap="sm">
      <Paper withBorder p="sm" data-testid="blocked-question">
        <Text size="xs" c="dimmed" mb={6}>
          {caption}
        </Text>
        <Text size="md" style={{ whiteSpace: 'pre-wrap' }}>
          {blocked.question}
        </Text>
        {blocked.kind === 'MergeConflict' && blocked.mergeTaskId && (
          <Anchor
            component={Link}
            to={`/orchestrator?tab=delegations&task=${blocked.mergeTaskId}`}
            size="sm"
            mt="xs"
            display="block"
          >
            Merge task {shortId(blocked.mergeTaskId)} is resolving it
          </Anchor>
        )}
      </Paper>

      <ReplySection detail={detail} blocked={blocked} autoFocus={autoFocus} onAnswered={onAnswered} />

      <Box data-testid="blocked-goal">
        <ClampedText label="Goal" text={detail.goal} />
      </Box>

      <SoFar blocked={blocked} variant="full" />
    </Stack>
  )
}

function CompactCard({
  detail,
  blocked,
  autoFocus,
  onAnswered,
}: {
  detail: AgentTaskDetailDto
  blocked: BlockedContextDto
  autoFocus?: boolean
  onAnswered?: () => void
}) {
  const [expanded, setExpanded] = useState(false)
  const [soFarOpen, setSoFarOpen] = useState(false)
  const hasSoFar =
    Boolean(blocked.context) ||
    Boolean(blocked.progress && !blocked.progress.unavailable) ||
    blocked.priorRounds.length > 0

  return (
    <Stack gap="xs">
      <UnstyledButton onClick={() => setExpanded((open) => !open)} data-testid="blocked-question">
        <Text size="sm" lineClamp={expanded ? undefined : 4} style={{ whiteSpace: 'pre-wrap' }}>
          {blocked.question}
        </Text>
      </UnstyledButton>
      <ReplySection detail={detail} blocked={blocked} autoFocus={autoFocus} onAnswered={onAnswered} />
      {hasSoFar && (
        <>
          <UnstyledButton onClick={() => setSoFarOpen((open) => !open)}>
            <Text size="xs" c="dimmed">
              {soFarOpen ? 'Hide so far' : 'So far'}
            </Text>
          </UnstyledButton>
          <Collapse in={soFarOpen}>
            <SoFar blocked={blocked} variant="compact" />
          </Collapse>
        </>
      )}
    </Stack>
  )
}

function ReplySection({
  detail,
  blocked,
  autoFocus,
  onAnswered,
}: {
  detail: AgentTaskDetailDto
  blocked: BlockedContextDto
  autoFocus?: boolean
  onAnswered?: () => void
}) {
  const { summary } = detail
  const [answer, setAnswer] = useState('')
  const [typedAtRound, setTypedAtRound] = useState<number | null>(null)
  const roundWhenOpened = useRef(blocked.round)
  const reply = useReplyToAgentTask()
  const cancel = useCancelAgentTask()
  const escalate = useEscalateAgentTask()
  const retry = useRetryAgentTask()
  const queryClient = useQueryClient()

  useEffect(() => {
    roundWhenOpened.current = blocked.round
  }, [blocked.round])

  const questionChanged = typedAtRound !== null && blocked.round !== typedAtRound
  const sendLabel = blocked.kind === 'MergeConflict' ? 'Tell the delegate' : 'Send answer'

  const send = () => {
    const message = answer.trim()
    if (!message || !blocked.canAnswer) return
    reply.mutate(
      { id: summary.id, message, round: blocked.round, origin: 'Web' },
      {
        onSuccess: () => {
          setAnswer('')
          void queryClient.invalidateQueries({ queryKey: attentionKeys.all })
          notifications.show({
            color: 'green',
            message: "Queued for the delegate's next idle moment",
          })
          onAnswered?.()
        },
        onError: (error: unknown) => {
          void queryClient.invalidateQueries({ queryKey: agentTaskKeys.detail(summary.id) })
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Could not deliver the answer'),
          })
        },
      },
    )
  }

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault()
      send()
    }
  }

  if (blocked.kind === 'CostCeiling' || blocked.kind === 'RoutingExhausted') {
    return (
      <Alert color="warning" title={blocked.kind === 'CostCeiling' ? 'Run cost ceiling' : 'Routing exhausted'}>
        <Text size="sm">{blocked.cannotAnswerReason ?? blocked.question}</Text>
        {blocked.kind === 'CostCeiling' && (
          <Text size="xs" c="dimmed" mt="xs">
            Governed by Delegation:MaxCostUsdPerRoot.
          </Text>
        )}
        <Group gap="xs" mt="sm">
          <Button
            size="xs"
            variant="light"
            color="danger"
            loading={cancel.isPending}
            onClick={() => cancel.mutate(summary.id, { onSuccess: onAnswered })}
          >
            Cancel
          </Button>
          <Button
            size="xs"
            variant="light"
            loading={escalate.isPending}
            onClick={() => escalate.mutate({ id: summary.id }, { onSuccess: onAnswered })}
          >
            Escalate
          </Button>
        </Group>
      </Alert>
    )
  }

  if (!blocked.canAnswer) {
    return (
      <Alert color="warning" title="Cannot answer">
        <Text size="sm">{blocked.cannotAnswerReason ?? "The delegate's session is no longer available."}</Text>
        <Button
          size="xs"
          variant="light"
          mt="sm"
          loading={retry.isPending}
          onClick={() => retry.mutate(summary.id, { onSuccess: onAnswered })}
        >
          Retry
        </Button>
      </Alert>
    )
  }

  return (
    <Stack gap={6} data-testid="blocked-reply">
      {questionChanged && (
        <Text size="xs" c="warning">
          The question changed since you started — check it still fits
        </Text>
      )}
      <Textarea
        autosize
        minRows={3}
        autoFocus={autoFocus}
        aria-label="Answer the delegate"
        placeholder="e.g. yes, accept negatives"
        value={answer}
        onChange={(event) => {
          const next = event.currentTarget.value
          setAnswer(next)
          if (typedAtRound === null && next.trim()) setTypedAtRound(blocked.round)
        }}
        onKeyDown={onKeyDown}
      />
      <Group justify="flex-end" gap="xs">
        {onAnswered && (
          <Button size="xs" variant="subtle" onClick={onAnswered}>
            Close
          </Button>
        )}
        <Button
          size="xs"
          leftSection={<TbSend size={14} />}
          loading={reply.isPending}
          disabled={!answer.trim()}
          onClick={send}
        >
          {sendLabel}
        </Button>
      </Group>
    </Stack>
  )
}

function SoFar({ blocked, variant }: { blocked: BlockedContextDto; variant: 'full' | 'compact' }) {
  const progress = blocked.progress
  const showDisk =
    progress &&
    !progress.unavailable &&
    (progress.commits.length > 0 || progress.changedFiles > 0 || progress.untrackedFiles > 0 || progress.branch)
  const showUnavailable = variant === 'full' && progress?.unavailable

  if (!blocked.context && !showDisk && !showUnavailable && blocked.priorRounds.length === 0) return null

  return (
    <Stack gap="sm" data-testid="blocked-so-far">
      {blocked.context && (
        <Box>
          <Label>Before it asked</Label>
          <ScrollArea.Autosize mah={240}>
            <RenderedMarkdown>{blocked.context}</RenderedMarkdown>
          </ScrollArea.Autosize>
        </Box>
      )}
      {showDisk && progress && (
        <Box>
          <Label>On disk</Label>
          <Text size="sm">
            {progress.branch ? `${progress.branch} · ` : ''}
            {progress.commits.length} commit{progress.commits.length === 1 ? '' : 's'}
            {progress.changedFiles ? ` · ${progress.changedFiles} files changed` : ''}
            {progress.untrackedFiles ? ` · ${progress.untrackedFiles} untracked` : ''}
          </Text>
          {progress.commits.map((commit) => (
            <Text key={commit} size="xs" c="dimmed" lineClamp={1}>
              {commit}
            </Text>
          ))}
          {progress.lastCheckDigest && (
            <Text size="xs" c="dimmed" mt={4}>
              Last check{progress.lastCheckAt ? ` ${formatClock(progress.lastCheckAt)}` : ''}:{' '}
              {progress.lastCheckDigest}
            </Text>
          )}
        </Box>
      )}
      {showUnavailable && progress?.unavailable && (
        <Text size="xs" c="dimmed">
          {progress.unavailable}
        </Text>
      )}
      {blocked.priorRounds.length > 0 && (
        <Box>
          <Label>Earlier rounds</Label>
          <Stack gap={4}>
            {blocked.priorRounds.map((round) => (
              <Text key={round.round} size="sm">
                Q ({formatClock(round.askedAt)}): {round.question}
                {round.answer
                  ? ` → A${round.answeredVia ? ` via ${round.answeredVia.toLowerCase()}` : ''}${
                      round.answeredAt ? ` (${formatClock(round.answeredAt)})` : ''
                    }: ${round.answer}`
                  : ''}
              </Text>
            ))}
          </Stack>
        </Box>
      )}
    </Stack>
  )
}

function ClampedText({ label, text }: { label: string; text: string }) {
  const [showAll, setShowAll] = useState(false)
  return (
    <Box>
      <Label>{label}</Label>
      <Text size="sm" lineClamp={showAll ? undefined : 4} style={{ whiteSpace: 'pre-wrap' }}>
        {text}
      </Text>
      {text.split('\n').length > 4 || text.length > 280 ? (
        <UnstyledButton onClick={() => setShowAll((open) => !open)}>
          <Text size="xs" c="dimmed">
            {showAll ? 'show less' : 'show all'}
          </Text>
        </UnstyledButton>
      ) : null}
    </Box>
  )
}

function Label({ children }: { children: ReactNode }) {
  return (
    <Text size="xs" c="dimmed" tt="uppercase" fw={700} mb={4}>
      {children}
    </Text>
  )
}

function formatClock(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}
