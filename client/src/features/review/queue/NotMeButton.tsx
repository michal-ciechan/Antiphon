import { Button, Group, Popover, Select, Stack, Text, Textarea } from '@mantine/core'
import { useState } from 'react'
import { TbHandStop } from 'react-icons/tb'

const REASONS = ['Not familiar with this area', 'Conflict of interest', 'Out / at capacity today', 'I wrote part of it', 'Other']

/** "Not me": returns the item to the team queue with a reason; the caller is excluded from the next pick. */
export function NotMeButton({
  teamKey,
  disabled,
  disabledReason,
  loading,
  onConfirm,
}: {
  teamKey: string | null
  disabled: boolean
  disabledReason?: string
  loading: boolean
  onConfirm: (reason: string) => void
}) {
  const [opened, setOpened] = useState(false)
  const [reason, setReason] = useState<string | null>(REASONS[0])
  const [note, setNote] = useState('')
  return (
    <Popover opened={opened} onChange={setOpened} width={290} position="bottom-end" withArrow shadow="md">
      <Popover.Target>
        <Button
          size="compact-xs"
          variant="default"
          leftSection={<TbHandStop size={12} />}
          onClick={() => setOpened((o) => !o)}
          disabled={disabled}
          title={disabled ? disabledReason : undefined}
          loading={loading}
        >
          Not me
        </Button>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap={6}>
          <Text size="xs">
            Return this item to the {teamKey ?? 'team'} queue. You're excluded from its next pick.
          </Text>
          <Select size="xs" aria-label="Reason" data={REASONS} value={reason} onChange={setReason} allowDeselect={false} comboboxProps={{ withinPortal: false }} />
          <Textarea size="xs" placeholder="Optional note for the team" autosize minRows={1} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Group justify="flex-end" gap={6}>
            <Button size="compact-xs" variant="subtle" color="gray" onClick={() => setOpened(false)}>
              Cancel
            </Button>
            <Button
              size="compact-xs"
              color="warning"
              onClick={() => {
                onConfirm(note.trim() ? `${reason ?? 'Other'}: ${note.trim()}` : (reason ?? 'Other'))
                setOpened(false)
                setNote('')
              }}
            >
              Pass back
            </Button>
          </Group>
        </Stack>
      </Popover.Dropdown>
    </Popover>
  )
}
