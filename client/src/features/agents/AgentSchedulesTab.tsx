import { useState } from 'react'
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Paper,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
  SegmentedControl,
  Chip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbPlayerPlay, TbTrash } from 'react-icons/tb'
import {
  formatNextLocal,
  useAgentSchedules,
  useCreateSchedule,
  useDeleteSchedule,
  useFireScheduleNow,
  usePatchSchedule,
  type ScheduleDto,
  type ScheduleRepeat,
} from '../../api/schedules'
import { getApiErrorMessage } from '../../api/client'

const WEEKDAYS = [
  { bit: 1, label: 'Mon' },
  { bit: 2, label: 'Tue' },
  { bit: 4, label: 'Wed' },
  { bit: 8, label: 'Thu' },
  { bit: 16, label: 'Fri' },
  { bit: 32, label: 'Sat' },
  { bit: 64, label: 'Sun' },
] as const

interface AgentSchedulesTabProps {
  agentId: string
  agentName?: string
}

export function AgentSchedulesTab({ agentId, agentName }: AgentSchedulesTabProps) {
  const list = useAgentSchedules(agentId)
  const patch = usePatchSchedule(agentId)
  const remove = useDeleteSchedule(agentId)
  const fireNow = useFireScheduleNow(agentId)
  const create = useCreateSchedule(agentId)

  const [name, setName] = useState('')
  const [prompt, setPrompt] = useState('')
  const [repeat, setRepeat] = useState<ScheduleRepeat>('Once')
  const [fireAt, setFireAt] = useState('')
  const [everyMinutes, setEveryMinutes] = useState('30')
  const [atLocal, setAtLocal] = useState('09:00')
  const [days, setDays] = useState(0)

  const onToggle = (row: ScheduleDto) => {
    patch.mutate(
      { id: row.id, body: { concurrencyToken: row.concurrencyToken, enabled: !row.enabled } },
      {
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not toggle the schedule') }),
      },
    )
  }

  const onDelete = (row: ScheduleDto) => {
    if (!window.confirm(`Delete schedule “${row.name}”? This cannot be undone.`)) return
    remove.mutate(row.id, {
      onError: (error) =>
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not delete the schedule') }),
    })
  }

  const onCreate = () => {
    const body: Parameters<typeof create.mutate>[0] = {
      name,
      kind: 'Prompt',
      repeat,
      agent: agentId,
      promptText: prompt,
    }
    if (repeat === 'Once') {
      if (!fireAt) {
        notifications.show({ color: 'red', message: 'Once requires a fire time.' })
        return
      }
      body.fireAt = new Date(fireAt).toISOString()
    } else if (repeat === 'Interval') {
      body.everyMinutes = Number(everyMinutes)
    } else {
      body.atLocal = atLocal
      if (days !== 0) body.daysOfWeek = days
    }
    create.mutate(body, {
      onSuccess: () => {
        setName('')
        setPrompt('')
      },
      onError: (error) =>
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not create the schedule') }),
    })
  }

  const rows = list.data?.schedules ?? []

  return (
    <Stack gap="md" data-testid="agent-schedules-tab">
      {rows.length === 0 && (
        <Text size="sm" c="dimmed">
          No schedules yet{agentName ? ` for ${agentName}` : ''}.
        </Text>
      )}
      {rows.map((row) => (
        <Paper key={row.id} withBorder p="xs" radius="sm" data-testid={`schedule-row-${row.id}`}>
          <Group justify="space-between" wrap="nowrap" align="flex-start">
            <Stack gap={4} style={{ minWidth: 0 }}>
              <Group gap="xs">
                <Text fw={600} size="sm">
                  {row.name}
                </Text>
                {row.lastOutcome && (
                  <Badge size="xs" variant="light" color={row.lastOutcome === 'Failed' || row.lastOutcome === 'Refused' ? 'red' : 'gray'}>
                    {row.lastOutcome}
                  </Badge>
                )}
              </Group>
              <Text size="xs" c="dimmed">
                {row.repeatDescription}
                {formatNextLocal(row) ? ` · next ${formatNextLocal(row)}` : ''}
              </Text>
            </Stack>
            <Group gap={4} wrap="nowrap">
              <Switch
                size="sm"
                checked={row.enabled}
                aria-label={`Toggle ${row.name}`}
                onChange={() => onToggle(row)}
                disabled={patch.isPending}
              />
              <ActionIcon
                variant="subtle"
                color="blue"
                aria-label={`Fire ${row.name} now`}
                onClick={() => fireNow.mutate(row.id)}
              >
                <TbPlayerPlay size={15} />
              </ActionIcon>
              <ActionIcon
                variant="subtle"
                color="red"
                aria-label={`Delete ${row.name}`}
                onClick={() => onDelete(row)}
              >
                <TbTrash size={15} />
              </ActionIcon>
            </Group>
          </Group>
        </Paper>
      ))}

      <Stack gap="xs">
        <Text size="sm" fw={600}>
          New schedule
        </Text>
        <TextInput label="Name" value={name} onChange={(e) => setName(e.currentTarget.value)} />
        <Textarea label="Prompt" minRows={3} value={prompt} onChange={(e) => setPrompt(e.currentTarget.value)} />
        <SegmentedControl
          value={repeat}
          onChange={(v) => setRepeat(v as ScheduleRepeat)}
          data={[
            { label: 'Once', value: 'Once' },
            { label: 'Interval', value: 'Interval' },
            { label: 'Daily', value: 'Daily' },
          ]}
        />
        {repeat === 'Once' && (
          <TextInput
            label="Fire at"
            type="datetime-local"
            value={fireAt}
            onChange={(e) => setFireAt(e.currentTarget.value)}
          />
        )}
        {repeat === 'Interval' && (
          <TextInput
            label="Every minutes"
            type="number"
            value={everyMinutes}
            onChange={(e) => setEveryMinutes(e.currentTarget.value)}
          />
        )}
        {repeat === 'Daily' && (
          <>
            <TextInput label="At local" value={atLocal} onChange={(e) => setAtLocal(e.currentTarget.value)} />
            <Group gap={6}>
              {WEEKDAYS.map((d) => (
                <Chip
                  key={d.bit}
                  size="xs"
                  checked={days === 0 || (days & d.bit) === d.bit}
                  onChange={() =>
                    setDays((current) => {
                      const next = current ^ d.bit
                      return next
                    })
                  }
                >
                  {d.label}
                </Chip>
              ))}
            </Group>
          </>
        )}
        <Button onClick={onCreate} loading={create.isPending} disabled={!name.trim() || !prompt.trim()}>
          Create
        </Button>
      </Stack>
    </Stack>
  )
}
