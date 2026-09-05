import { Button, Group, Select, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { getApiErrorMessage } from '../../api/client'
import { usePutModelAvailabilityHold } from '../../api/modelAvailability'

const KINDS = [
  { value: 'ClaudeCode', label: 'ClaudeCode' },
  { value: 'Grok', label: 'Grok' },
  { value: 'Codex', label: 'Codex' },
]

const ALIASES_BY_KIND: Record<string, { value: string; label: string }[]> = {
  ClaudeCode: [
    { value: '*', label: '* (kind-wide)' },
    { value: 'fable', label: 'fable' },
    { value: 'opus', label: 'opus' },
    { value: 'sonnet', label: 'sonnet' },
    { value: 'haiku', label: 'haiku' },
  ],
  Grok: [
    { value: '*', label: '* (kind-wide)' },
    { value: 'grok-4.6', label: 'grok-4.6' },
  ],
  Codex: [
    { value: '*', label: '* (kind-wide)' },
    { value: 'gpt-6-astra', label: 'gpt-6-astra' },
    { value: 'gpt-5.6-sol', label: 'gpt-5.6-sol' },
    { value: 'gpt-5.6-terra', label: 'gpt-5.6-terra' },
    { value: 'gpt-5.6-luna', label: 'gpt-5.6-luna' },
  ],
}

function toUtcIso(local: string): string {
  return new Date(local).toISOString()
}

/**
 * Shared Hold controls for the Orchestrator summary and the Routing tab.
 * Invalidation stays on usePutModelAvailabilityHold.
 */
export function ModelAvailabilityHoldForm() {
  const hold = usePutModelAvailabilityHold()
  const [kind, setKind] = useState<string | null>('ClaudeCode')
  const [alias, setAlias] = useState<string | null>('fable')
  const [untilLocal, setUntilLocal] = useState('')
  const [reason, setReason] = useState('')

  const aliases = ALIASES_BY_KIND[kind ?? 'ClaudeCode'] ?? ALIASES_BY_KIND.ClaudeCode

  const submitHold = () => {
    if (!kind || !alias) return
    const body: { disabledUntil?: string; reason?: string } = {}
    if (untilLocal.trim()) body.disabledUntil = toUtcIso(untilLocal)
    if (reason.trim()) body.reason = reason.trim()
    hold.mutate(
      { kind, alias, body },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: `${kind} ${alias} held` })
          setReason('')
        },
        onError: (error) =>
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Could not hold that model'),
          }),
      },
    )
  }

  return (
    <Group gap="xs" align="flex-end" wrap="wrap">
      <Select
        label="Kind"
        data={KINDS}
        value={kind}
        onChange={(value) => {
          setKind(value)
          const next = ALIASES_BY_KIND[value ?? '']?.[1]?.value ?? '*'
          setAlias(next)
        }}
        w={140}
      />
      <Select label="Alias" data={aliases} value={alias} onChange={setAlias} w={160} />
      <TextInput
        label="Until (local)"
        type="datetime-local"
        value={untilLocal}
        onChange={(event) => setUntilLocal(event.currentTarget.value)}
        w={210}
      />
      <TextInput
        label="Reason"
        value={reason}
        onChange={(event) => setReason(event.currentTarget.value)}
        placeholder="optional"
        style={{ flex: 1, minWidth: 160 }}
      />
      <Button onClick={submitHold} loading={hold.isPending} disabled={!kind || !alias}>
        Hold
      </Button>
    </Group>
  )
}
