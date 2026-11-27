import { Alert, Button, Group, Modal, Stack, Textarea } from '@mantine/core'
import { useState } from 'react'
import { ReviewProblemText } from '../components/ReviewProblem'
import { reviewProblem } from '../reviewErrors'
import { useCloseReviewRun } from '../useReview'

type CloseFormProps = { runId: string; concurrencyToken: number; onClose: () => void }

function CloseForm({ runId, concurrencyToken, onClose }: CloseFormProps) {
  const close = useCloseReviewRun(runId)
  const [reason, setReason] = useState('')

  return (
    <form onSubmit={(event) => {
      event.preventDefault()
      if (reason.trim()) close.mutate({ concurrencyToken, reason: reason.trim() }, { onSuccess: onClose })
    }}>
      <Stack gap="sm">
        <Textarea label="Close reason" value={reason} onChange={(event) => setReason(event.currentTarget.value)}
          required maxLength={4000} data-autofocus minRows={3} />
        {close.error && (
          <Alert color="red" title="Could not close the run">
            <ReviewProblemText problem={reviewProblem(close.error, 'The review API refused the close.')} />
          </Alert>
        )}
        <Group justify="flex-end" gap="xs">
          <Button size="xs" variant="subtle" color="gray" onClick={onClose}>Cancel</Button>
          <Button size="xs" type="submit" color="red" loading={close.isPending} disabled={!reason.trim()}>Close run</Button>
        </Group>
      </Stack>
    </form>
  )
}

export function CloseReviewRunModal({ opened, ...form }: CloseFormProps & { opened: boolean }) {
  return (
    <Modal opened={opened} onClose={form.onClose} title="Close review run">
      {opened && <CloseForm {...form} />}
    </Modal>
  )
}
