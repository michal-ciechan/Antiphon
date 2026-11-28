import { ActionIcon, Alert, Badge, Box, Button, CopyButton, Group, Select, Stack, Text, Tooltip } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import { lazy, useMemo, useState } from 'react'
import { TbAlertCircle, TbCopy, TbPlayerStop, TbRefresh } from 'react-icons/tb'
import { boardKeys, type AgentSessionSummaryDto } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'
import type { AgentSessionResumeMode } from '../../api/sessions'
import { useResumeSession } from '../../api/sessions'
import { SessionContextBadge } from '../agents/SessionContextBadge'
import { OperatorSessionStopDialog } from '../agents/OperatorSessionStopDialog'
import { SuspenseBoundary } from '../../shared/SuspenseBoundary'
import { loadSessionTerminal } from './sessionTerminalLoader'

const SessionTerminal = lazy(loadSessionTerminal)

interface SessionTabsProps {
  boardId: string
  sessions: AgentSessionSummaryDto[]
  compact?: boolean
  fill?: boolean
}

const STATUS_COLOR: Record<string, string> = {
  Running: 'green',
  Starting: 'yellow',
  Stopping: 'orange',
  Stopped: 'gray',
  Failed: 'red',
  Created: 'gray',
}

const CLAUDE_SESSION_NOT_FOUND_TEXT = 'Claude resume session was not found'

export function SessionTabs({ boardId, sessions, compact = false, fill = false }: SessionTabsProps) {
  const resumeSession = useResumeSession(boardId)
  const queryClient = useQueryClient()
  const [stopTarget, setStopTarget] = useState<string | null>(null)
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
  const canRecoverClaudeSession = canResume
    && (selectedSession.failureReason?.includes(CLAUDE_SESSION_NOT_FOUND_TEXT)
      || selectedSession.failureReason?.includes('No conversation found with session ID'))

  const resumeSelectedSession = (mode: AgentSessionResumeMode) => {
    if (!selectedSession) return

    resumeSession.mutate({ sessionId: selectedSession.id, mode }, {
      onSuccess: () => {
        const message = mode === 'Continue'
          ? 'Session continued from Claude context'
          : mode === 'New'
            ? 'New Claude session started'
            : 'Session resumed'
        notifications.show({ color: 'green', message })
      },
      onError: (error) => {
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Resume failed') })
      },
    })
  }

  if (sessions.length === 0) {
    return <Text size="sm" c="dimmed">No sessions</Text>
  }

  if (terminalSessions.length === 0) {
    return <Text size="sm" c="dimmed">No terminal sessions yet</Text>
  }

  return (
    <Stack gap={compact ? 6 : 'sm'} h={fill ? '100%' : undefined} style={{ minHeight: 0 }}>
      <Group align={compact ? 'center' : 'flex-end'} justify="space-between" gap="xs">
        <Select
          label={compact ? undefined : 'All sessions'}
          aria-label={compact ? 'All sessions' : undefined}
          data={sessionOptions}
          value={active}
          onChange={setSelectedSessionId}
          allowDeselect={false}
          searchable
          size={compact ? 'xs' : undefined}
          w={compact ? 220 : 260}
        />
        {selectedSession && (
          <Group gap={6} wrap="nowrap">
            <Badge size="sm" color={STATUS_COLOR[selectedSession.status] ?? 'gray'} variant="light">
              {selectedSession.status}
            </Badge>
            {selectedSession.agentKind === 'ClaudeCode' && (
              <SessionContextBadge
                fullness={selectedSession.contextFullness}
                state={selectedSession.contextFullnessState}
              />
            )}
            <CopyButton value={selectedSession.id}>
              {({ copied, copy }) => (
                <Tooltip label={copied ? 'Copied session id' : 'Copy session id'} withArrow>
                  {compact ? (
                    <ActionIcon size="sm" variant="subtle" onClick={copy} aria-label="Copy session id">
                      <TbCopy size={14} />
                    </ActionIcon>
                  ) : (
                    <Button
                      size="compact-xs"
                      variant="subtle"
                      leftSection={<TbCopy size={12} />}
                      onClick={copy}
                      aria-label="Copy session id"
                    >
                      ID
                    </Button>
                  )}
                </Tooltip>
              )}
            </CopyButton>
            {canResume && (
              compact ? (
                <Tooltip label="Resume session" withArrow>
                  <ActionIcon
                    size="sm"
                    variant="light"
                    loading={resumeSession.isPending}
                    aria-label="Resume"
                    onClick={() => resumeSelectedSession('Resume')}
                  >
                    <TbRefresh size={14} />
                  </ActionIcon>
                </Tooltip>
              ) : (
                <Button
                  size="compact-xs"
                  variant="light"
                  leftSection={<TbRefresh size={12} />}
                  loading={resumeSession.isPending}
                  onClick={() => resumeSelectedSession('Resume')}
                >
                  Resume
                </Button>
              )
            )}
            {canStop && (
              compact ? (
                <Tooltip label="Stop session" withArrow>
                  <ActionIcon
                    size="sm"
                    variant="light"
                    color="red"
                    aria-label="Stop"
                    onClick={() => setStopTarget(selectedSession.id)}
                  >
                    <TbPlayerStop size={14} />
                  </ActionIcon>
                </Tooltip>
              ) : (
                <Button
                  size="compact-xs"
                  variant="light"
                  color="red"
                  leftSection={<TbPlayerStop size={12} />}
                  onClick={() => setStopTarget(selectedSession.id)}
                >
                  Stop
                </Button>
              )
            )}
            <Text size="xs" c="dimmed" lineClamp={1} maw={compact ? 420 : 520}>
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
      {canRecoverClaudeSession && (
        <Alert
          color="yellow"
          variant="light"
          icon={<TbAlertCircle size={16} />}
          title="Claude session was not found"
          data-testid="claude-session-recovery"
        >
          <Stack gap="xs">
            <Text size="sm">
              Continue from the last Claude context in this worktree, or start a fresh Claude session for this card.
            </Text>
            <Group gap="xs">
              <Button
                size="xs"
                variant="filled"
                loading={resumeSession.isPending}
                onClick={() => resumeSelectedSession('Continue')}
              >
                Continue from context
              </Button>
              <Button
                size="xs"
                variant="light"
                loading={resumeSession.isPending}
                onClick={() => resumeSelectedSession('New')}
              >
                Start new session
              </Button>
            </Group>
          </Stack>
        </Alert>
      )}
      {stopTarget && (
        <OperatorSessionStopDialog sessionId={stopTarget} title={`Stop session ${stopTarget.slice(0, 8)}?`}
          confirmLabel="Confirm stop" onClose={() => setStopTarget(null)} onDone={() => {
            void queryClient.invalidateQueries({ queryKey: boardKeys.detail(boardId) })
            void queryClient.invalidateQueries({ queryKey: boardKeys.allDetails })
          }} />
      )}
      {selectedSession && (
        <Box style={{ flex: fill ? 1 : undefined, minHeight: fill ? 0 : undefined }}>
          <SuspenseBoundary variant="panel">
            <SessionTerminal session={selectedSession} fill={fill} />
          </SuspenseBoundary>
        </Box>
      )}
    </Stack>
  )
}
