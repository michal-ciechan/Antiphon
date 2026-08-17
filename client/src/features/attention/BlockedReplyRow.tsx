import { Button, Group, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { TbSend } from 'react-icons/tb'
import { useReplyToAgentTask } from '../../api/agentTasks'
import { attentionKeys } from '../../api/attention'
import { getApiErrorMessage } from '../../api/client'

/**
 * Answer a blocked delegate without leaving the list (CARD-0035 slice 4; CARD-0033's ask).
 *
 * <p><b>Why in place rather than a link to the drawer.</b> A blocked task is the one condition on
 * this view where the human IS the fix and the fix is two sentences long. Making that a navigation —
 * open the sibling tab, find the drawer, scroll to the answer box — is what turns "I'll just tell
 * it" into "I'll take the work back", and taking the work back throws away everything the delegate
 * has already read. The verb is the same `POST …/reply` the drawer uses; only the distance
 * changed.</p>
 *
 * <p>It posts through <c>useReplyToAgentTask</c> — the shared hook, not a second call site — so the
 * board and any open drawer are invalidated exactly as they are from the drawer's own form. This
 * adds the attention list to that invalidation, because a row that stays after its condition is gone
 * teaches the operator to distrust the whole view.</p>
 */
export function BlockedReplyRow({
  taskId,
  onDone,
}: {
  taskId: string
  /** Called after a successful send — the row collapses the form again. */
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
          notifications.show({ color: 'green', message: 'Answer sent to the delegate' })
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
