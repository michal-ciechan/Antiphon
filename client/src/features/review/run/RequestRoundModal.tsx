import { Alert, Button, Group, Modal, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { ReviewProblemText } from '../components/ReviewProblem'
import { reviewProblem } from '../reviewErrors'
import { useRequestReviewRound } from '../useReview'

/** The server's limit on a steer prompt. */
const MAX_STEER = 4000

function RoundForm({ runId, onDone }: { runId: string; onDone: () => void }) {
  const request = useRequestReviewRound(runId)
  const [steer, setSteer] = useState('')
  const problem = request.error ? reviewProblem(request.error, 'The review API refused the round.') : null

  return (
    <form
      data-testid="review-round-form"
      onSubmit={(e) => {
        e.preventDefault()
        const steerPrompt = steer.trim()
        request.mutate(steerPrompt ? { steerPrompt } : {}, {
          onSuccess: (run) => {
            notifications.show({ color: 'gray', message: `Round ${run.round} queued${steerPrompt ? ' with your steer' : ''}.` })
            onDone()
          },
        })
      }}
    >
      <Stack gap="sm">
        <Textarea
          size="xs"
          label="Steer prompt"
          description="Optional. The reviewers read it as a human steer."
          placeholder="Focus on the retry path in the lease store."
          autosize
          minRows={3}
          maxRows={8}
          maxLength={MAX_STEER}
          data-autofocus
          data-testid="review-round-steer"
          value={steer}
          onChange={(e) => setSteer(e.currentTarget.value)}
        />
        {problem && (
          <Alert color="danger" variant="light" p={8} title={<Text size="xs" fw={700}>Not queued</Text>} data-testid="review-round-error">
            <ReviewProblemText problem={problem} />
          </Alert>
        )}
        <Text size="xs" c="dimmed">
          Antiphon reviews the current head again in the same round. A run waiting at a gate takes another round from the gate instead.
        </Text>
        <Group justify="flex-end" gap="xs">
          <Button size="xs" variant="subtle" color="gray" onClick={onDone}>
            Cancel
          </Button>
          <Button size="xs" type="submit" loading={request.isPending} data-testid="review-round-submit">
            Request round
          </Button>
        </Group>
      </Stack>
    </form>
  )
}

/** `POST /api/review-runs/{id}/rounds`: a review round with an optional steer; a refusal (409) shows in the modal. */
export function RequestRoundModal({ runId, opened, onClose }: { runId: string; opened: boolean; onClose: () => void }) {
  return (
    <Modal opened={opened} onClose={onClose} title={<Text fw={700}>Request review round</Text>} size="md">
      {/* mounted only while open, so each opening starts from a clean form and no stale refusal */}
      {opened && <RoundForm runId={runId} onDone={onClose} />}
    </Modal>
  )
}
