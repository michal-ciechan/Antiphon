import { Button, Group, Modal, PasswordInput, Stack, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { getApiErrorMessage } from '../../api/client'
import { stopSession } from '../../api/sessions'

export function OperatorSessionStopDialog({ sessionId, title, confirmLabel, cancelLabel = 'Cancel', children, onClose, onDone }: {
  sessionId: string
  title: string
  confirmLabel: string
  cancelLabel?: string
  children?: ReactNode
  onClose: () => void
  onDone: () => void
}) {
  const [operatorToken, setOperatorToken] = useState('')
  const [reason, setReason] = useState('')
  const stop = useMutation({
    mutationFn: () => stopSession({ sessionId, reason: reason.trim(), operatorToken: operatorToken.trim() }),
    onSuccess: () => {
      onClose()
      onDone()
      notifications.show({ color: 'green', message: 'Session stop requested' })
    },
    onError: (error) => {
      setOperatorToken('')
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not stop the session') })
    },
  })
  const pending = stop.isPending
  return (
    <Modal opened onClose={onClose} title={title} centered closeOnClickOutside={!pending} closeOnEscape={!pending}>
      <Stack gap="sm">
        {children}
        <Textarea label="Reason" value={reason} maxLength={1000} disabled={pending}
          onChange={(event) => setReason(event.currentTarget.value)} />
        <PasswordInput label="Operator token" value={operatorToken} disabled={pending}
          onChange={(event) => setOperatorToken(event.currentTarget.value)} />
        <Group justify="flex-end" gap="xs">
          <Button size="xs" variant="subtle" disabled={pending} onClick={onClose}>{cancelLabel}</Button>
          <Button size="xs" color="red" loading={pending} disabled={!reason.trim() || !operatorToken.trim()}
            onClick={() => stop.mutate()}>
            {confirmLabel}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
