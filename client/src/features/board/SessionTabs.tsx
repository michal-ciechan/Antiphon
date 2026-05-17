import { Badge, Group, Select, Stack, Text } from '@mantine/core'
import { useMemo, useState } from 'react'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { SessionTerminal } from './SessionTerminal'

interface SessionTabsProps {
  sessions: AgentSessionSummaryDto[]
}

const STATUS_COLOR: Record<string, string> = {
  Running: 'green',
  Starting: 'yellow',
  Stopping: 'orange',
  Stopped: 'gray',
  Failed: 'red',
  Created: 'gray',
}

export function SessionTabs({ sessions }: SessionTabsProps) {
  const terminalSessions = useMemo(
    () => sessions.filter((session) => session.id && session.cwd.trim().length > 0),
    [sessions],
  )
  const hiddenSessionCount = sessions.length - terminalSessions.length
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null)
  const active = terminalSessions.some((session) => session.id === selectedSessionId)
    ? selectedSessionId
    : terminalSessions[0]?.id ?? null
  const selectedSession = terminalSessions.find((session) => session.id === active) ?? null
  const sessionOptions = terminalSessions.map((session, index) => ({
    value: session.id,
    label: `Session ${terminalSessions.length - index} - ${session.status}`,
  }))

  if (sessions.length === 0) {
    return <Text size="sm" c="dimmed">No sessions</Text>
  }

  if (terminalSessions.length === 0) {
    return <Text size="sm" c="dimmed">No terminal sessions yet</Text>
  }

  return (
    <Stack gap="sm">
      <Group align="flex-end" justify="space-between">
        <Select
          label="All sessions"
          data={sessionOptions}
          value={active}
          onChange={setSelectedSessionId}
          allowDeselect={false}
          searchable
          w={260}
        />
        {selectedSession && (
          <Group gap={6} wrap="nowrap">
            <Badge size="sm" color={STATUS_COLOR[selectedSession.status] ?? 'gray'} variant="light">
              {selectedSession.status}
            </Badge>
            <Text size="xs" c="dimmed" lineClamp={1} maw={520}>
              {selectedSession.definitionName} · {selectedSession.cwd}
            </Text>
          </Group>
        )}
      </Group>
      {hiddenSessionCount > 0 && (
        <Text size="xs" c="dimmed" data-testid="hidden-session-count">
          Hidden {hiddenSessionCount} preparing session{hiddenSessionCount === 1 ? '' : 's'} without a terminal.
        </Text>
      )}
      {active && <SessionTerminal sessionId={active} />}
    </Stack>
  )
}
