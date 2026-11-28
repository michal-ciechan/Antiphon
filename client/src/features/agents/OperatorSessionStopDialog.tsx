import { Button, Group, Modal, PasswordInput, Stack, Textarea } from '@mantine/core'
import { useState, type ReactNode } from 'react'

export function OperatorSessionStopDialog({ title, confirmLabel, cancelLabel = 'Cancel', children, pending, onClose, onConfirm }: {
  title: string
  confirmLabel: string
  cancelLabel?: string
  children?: ReactNode
  pending: boolean
  onClose: () => void
  onConfirm: (reason: string, operatorToken: string) => void
}) {
  const [operatorToken, setOperatorToken] = useState('')
  const [reason, setReason] = useState('')
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
            onClick={() => onConfirm(reason.trim(), operatorToken.trim())}>
            {confirmLabel}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
