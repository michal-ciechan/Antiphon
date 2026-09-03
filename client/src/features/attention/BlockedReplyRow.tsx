import { Button, Group, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { TbSend } from 'react-icons/tb'
import { useAgentTask, useReplyToAgentTask } from '../../api/agentTasks'
import { attentionKeys } from '../../api/attention'
import { getApiErrorMessage } from '../../api/client'
import { BlockedQuestionCard } from '../delegations/BlockedQuestionCard'

/**
 * Answer a blocked delegate without leaving the list (CARD-0035 slice 4; CARD-0033's ask).
 *
 * <p>When the task detail fetch succeeds, this is the compact <c>BlockedQuestionCard</c>. If that
 * fetch fails, it falls back to the bare box so an answer is never blocked by the context
 * request.</p>
 */
export function BlockedReplyRow({
  taskId,
  onDone,
  evidence,
}: {
  taskId: string
  /** Called after a successful send — the row collapses the form again. */
  onDone?: () => void
  /** Fallback question text when the detail fetch fails. */
  evidence?: string
}) {
  const detail = useAgentTask(taskId)

  if (detail.data?.blocked) {
    return (
      <BlockedQuestionCard
        detail={detail.data}
        variant="compact"
        autoFocus
        onAnswered={onDone}
      />
    )
  }

  return <BareBlockedReply taskId={taskId} evidence={evidence} onDone={onDone} />
}

function BareBlockedReply({
  taskId,
  evidence,
  onDone,
}: {
  taskId: string
  evidence?: string
  onDone?: () => void
}) {
  const [answer, setAnswer] = useState('')
  const reply = useReplyToAgentTask()
  const queryClient = useQueryClient()

  const send = () =>
    reply.mutate(
      { id: taskId, message: answer.trim() },
      {
        onSuccess: () => {
          setAnswer('')
          void queryClient.invalidateQueries({ queryKey: attentionKeys.all })
          notifications.show({
            color: 'green',
            message: "Queued for the delegate's next idle moment",
          })
          onDone?.()
        },
        onError: (error: unknown) =>
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Could not deliver the answer'),
          }),
      },
    )

  return (
    <Stack gap={6} data-testid="blocked-reply">
      {evidence && (
        <Text size="sm" lineClamp={4} style={{ whiteSpace: 'pre-wrap' }}>
          {evidence}
        </Text>
      )}
      <Text size="xs" c="dimmed">
        Answer the question rather than taking the work back — the delegate keeps its context and
        carries on from where it stopped.
      </Text>
      <Textarea
        autosize
        minRows={2}
        aria-label="Answer the delegate"
        placeholder="e.g. yes, accept negatives"
        value={answer}
        onChange={(event) => setAnswer(event.currentTarget.value)}
      />
      <Group justify="flex-end" gap="xs">
        <Button size="xs" variant="subtle" onClick={() => onDone?.()}>
          Close
        </Button>
        <Button
          size="xs"
          leftSection={<TbSend size={14} />}
          loading={reply.isPending}
          disabled={!answer.trim()}
          onClick={send}
        >
          Send answer
        </Button>
      </Group>
    </Stack>
  )
}
