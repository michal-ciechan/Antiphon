import { Button, Modal, PasswordInput, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { getApiErrorMessage } from '../../api/client'
import { abandonQueuedMessage } from '../../api/sessions'

export function AbandonMessageDialog({ sessionId, messageId, opened, onClose, onDone }: {
  sessionId: string
  messageId: string
  opened: boolean
  onClose: () => void
  onDone: () => void | Promise<void>
}) {
  const [reason, setReason] = useState('')
  const [operatorToken, setOperatorToken] = useState('')
  const close = () => {
    setOperatorToken('')
    setReason('')
    onClose()
  }
  const abandon = useMutation({
    mutationFn: () => abandonQueuedMessage(sessionId, messageId, reason, operatorToken),
    onSuccess: async (result) => {
      close()
      await onDone()
      notifications.show({ color: 'orange', message: result.warning })
    },
    onError: (error) => notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not abandon the message') }),
  })
  return (
    <Modal opened={opened} onClose={close} title="Abandon unconfirmed message?" closeOnClickOutside={!abandon.isPending} closeOnEscape={!abandon.isPending}>
      <Stack>
        <Text>This message may have been delivered to the agent. Abandoning does not recall it. No input will be resent.</Text>
        <Textarea label="Reason" value={reason} maxLength={1000} disabled={abandon.isPending} onChange={(event) => setReason(event.currentTarget.value)} />
        <PasswordInput label="Operator token" value={operatorToken} disabled={abandon.isPending} onChange={(event) => setOperatorToken(event.currentTarget.value)} />
        <Button color="red" loading={abandon.isPending} disabled={!reason.trim() || !operatorToken.trim()} onClick={() => abandon.mutate()}>
          Confirm abandonment
        </Button>
      </Stack>
    </Modal>
  )
}
