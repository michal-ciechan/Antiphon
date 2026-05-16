import { Badge, Group, Tabs, Text } from '@mantine/core'
import { useEffect, useState } from 'react'
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
  const [active, setActive] = useState<string | null>(sessions[0]?.id ?? null)

  useEffect(() => {
    if (!active || !sessions.some((session) => session.id === active)) {
      setActive(sessions[0]?.id ?? null)
    }
  }, [active, sessions])

  if (sessions.length === 0) {
    return <Text size="sm" c="dimmed">No sessions</Text>
  }

  return (
    <Tabs value={active} onChange={setActive} keepMounted={false}>
      <Tabs.List>
        {sessions.map((session, index) => (
          <Tabs.Tab key={session.id} value={session.id}>
            <Group gap={6} wrap="nowrap">
              <Text size="sm">Session {sessions.length - index}</Text>
              <Badge size="xs" color={STATUS_COLOR[session.status] ?? 'gray'} variant="light">
                {session.status}
              </Badge>
            </Group>
          </Tabs.Tab>
        ))}
      </Tabs.List>

      {sessions.map((session) => (
        <Tabs.Panel key={session.id} value={session.id} pt="sm">
          <SessionTerminal sessionId={session.id} />
        </Tabs.Panel>
      ))}
    </Tabs>
  )
}
