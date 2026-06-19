import { useEffect, useRef, useState } from 'react'
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Paper,
  SegmentedControl,
  Stack,
  Text,
  Textarea,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { TbSend, TbTrash, TbClock } from 'react-icons/tb'
import {
  cancelQueuedMessage,
  enqueueSessionMessage,
  getSessionQueue,
  sendQueuedMessageNow,
  type MessageSendMode,
  type SessionQueueDto,
} from '../../api/sessions'
import { getApiErrorMessage } from '../../api/client'

const HUB_URL = '/hubs/antiphon'

interface SessionMessageQueueProps {
  sessionId: string
}

/**
 * Compose-and-queue panel for a live agent session. A message is either sent immediately ("Now")
 * or held until the agent finishes its current turn ("When idle"), at which point the server flushes
 * the oldest queued message. Pending messages are listed with per-item "send now" and remove actions.
 */
export function SessionMessageQueue({ sessionId }: SessionMessageQueueProps) {
  const [queue, setQueue] = useState<SessionQueueDto>({ sessionId, messages: [], working: false })
  const [body, setBody] = useState('')
  const [mode, setMode] = useState<MessageSendMode>('WhenIdle')
  const [busy, setBusy] = useState(false)
  const queueRef = useRef(queue)
  queueRef.current = queue

  useEffect(() => {
    let disposed = false
    const groupName = `session-${sessionId}`
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    const onQueueChanged = (payload: SessionQueueDto) => {
      if (payload.sessionId === sessionId && !disposed) setQueue(payload)
    }
    // The queue empties (and Working flips) on finish too — refetch to reflect it.
    const onFinished = (payload: { sessionId: string }) => {
      if (payload.sessionId === sessionId) void load()
    }
    connection.on('SessionQueueChanged', onQueueChanged)
    connection.on('SessionFinished', onFinished)
    connection.onreconnected(() => {
      void connection.invoke('JoinGroup', groupName).then(load)
    })

    const load = async () => {
      try {
        const data = await getSessionQueue(sessionId)
        if (!disposed) setQueue(data)
      } catch {
        /* keep whatever streamed live */
      }
    }

    void (async () => {
      try {
        await connection.start()
        await connection.invoke('JoinGroup', groupName)
      } catch {
        /* still load over HTTP */
      }
      await load()
    })()

    return () => {
      disposed = true
      connection.off('SessionQueueChanged', onQueueChanged)
      connection.off('SessionFinished', onFinished)
      if (connection.state === HubConnectionState.Connected) {
        void connection.invoke('LeaveGroup', groupName).finally(() => void connection.stop())
      } else {
        void connection.stop()
      }
    }
  }, [sessionId])

  const run = async (action: () => Promise<SessionQueueDto>, fallback: string) => {
    setBusy(true)
    try {
      setQueue(await action())
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, fallback) })
    } finally {
      setBusy(false)
    }
  }

  const submit = async () => {
    const text = body.trim()
    if (!text) return
    await run(() => enqueueSessionMessage(sessionId, text, mode), 'Could not send the message')
    setBody('')
  }

  const { messages, working } = queue
  const statusBadge = working
    ? { color: 'yellow', label: 'Working…' }
    : messages.length > 0
      ? { color: 'blue', label: 'Idle — flushing queue' }
      : { color: 'green', label: 'Idle / waiting' }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Badge color={statusBadge.color} variant="light">
          {statusBadge.label}
        </Badge>
        <Text size="xs" c="dimmed">
          {messages.length} queued
        </Text>
      </Group>

      <Stack gap="xs">
        <Textarea
          placeholder="Message to the agent…"
          autosize
          minRows={2}
          maxRows={6}
          value={body}
          onChange={(e) => setBody(e.currentTarget.value)}
          onKeyDown={(e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
              e.preventDefault()
              void submit()
            }
          }}
        />
        <Group justify="space-between">
          <SegmentedControl
            size="xs"
            value={mode}
            onChange={(v) => setMode(v as MessageSendMode)}
            data={[
              { label: 'When idle', value: 'WhenIdle' },
              { label: 'Send now', value: 'Now' },
            ]}
          />
          <Button
            size="xs"
            leftSection={mode === 'Now' ? <TbSend size={14} /> : <TbClock size={14} />}
            loading={busy}
            disabled={!body.trim()}
            onClick={() => void submit()}
          >
            {mode === 'Now' ? 'Send now' : 'Queue'}
          </Button>
        </Group>
        <Text size="xs" c="dimmed">
          {mode === 'Now'
            ? 'Delivered immediately, even mid-task.'
            : 'Held until the agent finishes its current turn, then delivered automatically.'}{' '}
          Ctrl/⌘+Enter to send.
        </Text>
      </Stack>

      {messages.length > 0 && (
        <Stack gap={6}>
          {messages.map((m, i) => (
            <Paper key={m.id} withBorder p="xs" radius="sm">
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <Group gap="xs" wrap="nowrap" align="flex-start" style={{ minWidth: 0 }}>
                  <Badge size="xs" variant="default" color="gray">
                    {i + 1}
                  </Badge>
                  <Text size="sm" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                    {m.body}
                  </Text>
                </Group>
                <Group gap={4} wrap="nowrap">
                  <Tooltip label="Send now">
                    <ActionIcon
                      variant="subtle"
                      color="blue"
                      disabled={busy}
                      onClick={() =>
                        void run(() => sendQueuedMessageNow(sessionId, m.id), 'Could not send the message')
                      }
                    >
                      <TbSend size={15} />
                    </ActionIcon>
                  </Tooltip>
                  <Tooltip label="Remove">
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      disabled={busy}
                      onClick={() =>
                        void run(() => cancelQueuedMessage(sessionId, m.id), 'Could not remove the message')
                      }
                    >
                      <TbTrash size={15} />
                    </ActionIcon>
                  </Tooltip>
                </Group>
              </Group>
            </Paper>
          ))}
        </Stack>
      )}
    </Stack>
  )
}
