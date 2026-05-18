import { Badge, Button, CopyButton, Group, Select, Stack, Text, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import { TbCopy, TbPlayerStop, TbRefresh } from 'react-icons/tb'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { useResumeSession, useStopSession } from '../../api/sessions'
import { SessionTerminal } from './SessionTerminal'

interface SessionTabsProps {
  boardId: string
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

export function SessionTabs({ boardId, sessions }: SessionTabsProps) {
  const resumeSession = useResumeSession(boardId)
  const stopSession = useStopSession(boardId)
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
  const canResume = selectedSession?.agentKind === 'ClaudeCode'
    && (selectedSession.status === 'Stopped' || selectedSession.status === 'Failed')
  const canStop = selectedSession?.status === 'Running' || selectedSession?.status === 'Starting'

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
            <CopyButton value={selectedSession.id}>
              {({ copied, copy }) => (
                <Tooltip label={copied ? 'Copied session id' : 'Copy session id'} withArrow>
                  <Button
                    size="compact-xs"
                    variant="subtle"
                    leftSection={<TbCopy size={12} />}
                    onClick={copy}
                    aria-label="Copy session id"
                  >
                    ID
                  </Button>
                </Tooltip>
              )}
            </CopyButton>
            {canResume && (
              <Button
                size="compact-xs"
                variant="light"
                leftSection={<TbRefresh size={12} />}
                loading={resumeSession.isPending}
                onClick={() => {
                  resumeSession.mutate(selectedSession.id, {
                    onSuccess: () => notifications.show({ color: 'green', message: 'Session resumed' }),
                    onError: (error) => {
                      notifications.show({ color: 'red', message: error instanceof Error ? error.message : 'Resume failed' })
                    },
                  })
                }}
              >
                Resume
              </Button>
            )}
            {canStop && (
              <Button
                size="compact-xs"
                variant="light"
                color="red"
                leftSection={<TbPlayerStop size={12} />}
                loading={stopSession.isPending}
                onClick={() => {
                  stopSession.mutate(selectedSession.id, {
                    onSuccess: () => notifications.show({ color: 'green', message: 'Session stopped' }),
                    onError: (error) => {
                      notifications.show({ color: 'red', message: error instanceof Error ? error.message : 'Stop failed' })
                    },
                  })
                }}
              >
                Stop
              </Button>
            )}
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
      {selectedSession && <SessionTerminal session={selectedSession} />}
    </Stack>
  )
}
