import { useEffect, useMemo, useRef, useState } from 'react'
import {
  ActionIcon,
  Badge,
  Box,
  Code,
  Collapse,
  Divider,
  Group,
  Loader,
  Paper,
  ScrollArea,
  Stack,
  Text,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import {
  TbBrain,
  TbCheck,
  TbChevronRight,
  TbCopy,
  TbExclamationCircle,
  TbTool,
  TbUser,
} from 'react-icons/tb'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import {
  getSessionTranscript,
  type SessionTranscriptPayload,
  type TranscriptEntryDto,
} from '../../api/sessions'

const HUB_URL = '/hubs/antiphon'

interface Turn {
  key: string
  prompt?: TranscriptEntryDto
  title?: string
  items: TranscriptEntryDto[] // thinking / tool calls / assistant text, in order
  ended?: TranscriptEntryDto
}

// Group the flat entry stream into turns. A new turn starts at each user prompt; tool results are
// folded into their originating tool call (matched by toolUseId) at render time.
function buildTurns(entries: TranscriptEntryDto[]): Turn[] {
  const turns: Turn[] = []
  let current: Turn | null = null

  for (const e of entries) {
    if (e.kind === 'UserPrompt') {
      current = { key: `turn-${e.sequence}`, prompt: e, items: [] }
      turns.push(current)
      continue
    }
    if (!current) {
      current = { key: 'turn-pre', items: [] }
      turns.push(current)
    }
    if (e.kind === 'TurnTitle') {
      if (!current.title) current.title = e.text ?? undefined
      continue
    }
    if (e.kind === 'TurnEnd') {
      current.ended = e
      continue
    }
    current.items.push(e)
  }

  return turns
}

// Idle once the latest meaningful entry is a TurnEnd; working while activity outranks the last end.
// CompactBoundary is idle-time housekeeping, not activity (mirror of the server's IsWorkingAsync —
// counting it would show a phantom "working" agent after every compaction).
// Exported for tests: the exclusion list must stay in lockstep with the server.
export function isWorking(entries: TranscriptEntryDto[]): boolean {
  let lastActivity = 0
  let lastEnd = 0
  for (const e of entries) {
    if (e.kind === 'TurnEnd') lastEnd = Math.max(lastEnd, e.sequence)
    else if (e.kind !== 'TurnTitle' && e.kind !== 'CompactBoundary')
      lastActivity = Math.max(lastActivity, e.sequence)
  }
  return lastActivity > lastEnd
}

function summarizeToolInput(toolInput: string | null): string {
  if (!toolInput) return ''
  try {
    const obj = JSON.parse(toolInput) as Record<string, unknown>
    const key = ['command', 'file_path', 'pattern', 'description', 'prompt', 'query', 'skill'].find(
      (k) => typeof obj[k] === 'string',
    )
    if (key) return String(obj[key])
  } catch {
    /* not JSON — fall through */
  }
  return toolInput
}

function ThinkingRow({ entry }: { entry: TranscriptEntryDto }) {
  const [open, { toggle }] = useDisclosure(false)
  return (
    <Box>
      <UnstyledButton onClick={toggle}>
        <Group gap={6} c="dimmed">
          <TbChevronRight
            size={13}
            style={{ transform: open ? 'rotate(90deg)' : 'none', transition: 'transform 120ms' }}
          />
          <TbBrain size={13} />
          <Text size="xs" fs="italic">
            Thinking
          </Text>
        </Group>
      </UnstyledButton>
      <Collapse in={open}>
        <Text size="xs" c="dimmed" fs="italic" pl={26} style={{ whiteSpace: 'pre-wrap' }}>
          {entry.text}
        </Text>
      </Collapse>
    </Box>
  )
}

function ToolRow({ call, result }: { call: TranscriptEntryDto; result?: TranscriptEntryDto }) {
  const [open, { toggle }] = useDisclosure(false)
  const isError = result?.toolIsError === true
  return (
    <Box>
      <UnstyledButton onClick={toggle} style={{ width: '100%' }}>
        <Group gap={6} wrap="nowrap">
          <TbChevronRight
            size={13}
            style={{ transform: open ? 'rotate(90deg)' : 'none', transition: 'transform 120ms' }}
          />
          <TbTool size={13} color="var(--mantine-color-violet-4)" />
          <Text size="xs" fw={600} c="violet.3" style={{ whiteSpace: 'nowrap' }}>
            {call.toolName ?? 'tool'}
          </Text>
          <Text size="xs" c="dimmed" lineClamp={1}>
            {summarizeToolInput(call.toolInput)}
          </Text>
          {result &&
            (isError ? (
              <TbExclamationCircle size={13} color="var(--mantine-color-red-5)" />
            ) : (
              <TbCheck size={13} color="var(--mantine-color-green-5)" />
            ))}
        </Group>
      </UnstyledButton>
      <Collapse in={open}>
        <Stack gap={4} pl={26} pt={4}>
          {call.toolInput && (
            <Code block fz="xs">
              {call.toolInput}
            </Code>
          )}
          {result?.text && (
            <Code block fz="xs" color={isError ? 'red' : undefined}>
              {result.text}
            </Code>
          )}
        </Stack>
      </Collapse>
    </Box>
  )
}

export function SessionTranscriptPanel({ sessionId }: { sessionId: string }) {
  const [entries, setEntries] = useState<TranscriptEntryDto[]>([])
  const [loading, setLoading] = useState(true)
  const seqRef = useRef<Set<number>>(new Set())

  useEffect(() => {
    let disposed = false
    seqRef.current = new Set()
    setEntries([])
    setLoading(true)

    const merge = (incoming: TranscriptEntryDto[]) => {
      const fresh = incoming.filter((e) => !seqRef.current.has(e.sequence))
      if (fresh.length === 0) return
      fresh.forEach((e) => seqRef.current.add(e.sequence))
      setEntries((prev) => [...prev, ...fresh].sort((a, b) => a.sequence - b.sequence))
    }

    const load = async () => {
      try {
        const data = await getSessionTranscript(sessionId)
        if (!disposed) merge(data.entries)
      } catch {
        /* keep whatever streamed live */
      } finally {
        if (!disposed) setLoading(false)
      }
    }

    const groupName = `session-${sessionId}`
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    const onEntry = (payload: SessionTranscriptPayload) => {
      if (payload.sessionId === sessionId) merge([payload])
    }
    connection.on('SessionTranscript', onEntry)
    connection.onreconnected(() => {
      void connection.invoke('JoinGroup', groupName).then(load)
    })

    void (async () => {
      try {
        await connection.start()
        await connection.invoke('JoinGroup', groupName)
      } catch {
        /* backlog still loads over HTTP */
      }
      await load()
    })()

    return () => {
      disposed = true
      connection.off('SessionTranscript', onEntry)
      if (connection.state === HubConnectionState.Connected) {
        void connection.invoke('LeaveGroup', groupName).finally(() => void connection.stop())
      } else {
        void connection.stop()
      }
    }
  }, [sessionId])

  const turns = useMemo(() => buildTurns(entries), [entries])
  const working = useMemo(() => isWorking(entries), [entries])
  const resultsByToolUse = useMemo(() => {
    const m = new Map<string, TranscriptEntryDto>()
    for (const e of entries) if (e.kind === 'ToolResult' && e.toolUseId) m.set(e.toolUseId, e)
    return m
  }, [entries])

  const latestAnswer = useMemo(() => {
    for (let i = turns.length - 1; i >= 0; i--) {
      const text = turns[i].items
        .filter((e) => e.kind === 'AssistantText')
        .map((e) => e.text)
        .filter(Boolean)
        .join('\n\n')
      if (text) return text
    }
    return ''
  }, [turns])

  const copyAnswer = () => {
    if (!latestAnswer) return
    void navigator.clipboard.writeText(latestAnswer)
    notifications.show({ message: 'Final output copied', color: 'green' })
  }

  return (
    <Stack gap="xs" style={{ minHeight: 0 }}>
      <Group justify="space-between">
        <Group gap="xs">
          <Badge color={working ? 'yellow' : 'green'} variant="light">
            {working ? 'Working…' : 'Idle'}
          </Badge>
          <Text size="xs" c="dimmed">
            {turns.length} turn{turns.length === 1 ? '' : 's'}
          </Text>
        </Group>
        <Tooltip label="Copy the latest answer (including any that scrolled off the terminal)">
          <ActionIcon variant="subtle" disabled={!latestAnswer} onClick={copyAnswer} aria-label="Copy final output">
            <TbCopy size={16} />
          </ActionIcon>
        </Tooltip>
      </Group>

      <ScrollArea h={460} type="auto" offsetScrollbars>
        {loading && entries.length === 0 ? (
          <Group justify="center" py="xl">
            <Loader size="sm" />
          </Group>
        ) : entries.length === 0 ? (
          <Text size="sm" c="dimmed" ta="center" py="xl">
            No transcript yet. Send the agent a prompt and the structured turn-by-turn flow appears here.
          </Text>
        ) : (
          <Stack gap="md" pr="xs">
            {turns.map((turn) => (
              <Paper key={turn.key} withBorder p="sm" radius="md">
                <Stack gap="xs">
                  {turn.prompt && (
                    <Group gap={6} align="flex-start" wrap="nowrap">
                      <TbUser size={15} style={{ marginTop: 3, flexShrink: 0 }} color="var(--mantine-color-blue-4)" />
                      <Text size="sm" fw={500} style={{ whiteSpace: 'pre-wrap' }}>
                        {turn.prompt.text}
                      </Text>
                    </Group>
                  )}
                  {turn.title && (
                    <Badge size="xs" variant="dot" color="gray" style={{ alignSelf: 'flex-start' }}>
                      {turn.title}
                    </Badge>
                  )}

                  {turn.items.map((item) => {
                    if (item.kind === 'Thinking') return <ThinkingRow key={item.sequence} entry={item} />
                    if (item.kind === 'ToolCall')
                      return (
                        <ToolRow
                          key={item.sequence}
                          call={item}
                          result={item.toolUseId ? resultsByToolUse.get(item.toolUseId) : undefined}
                        />
                      )
                    if (item.kind === 'AssistantText')
                      return (
                        <Text key={item.sequence} size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                          {item.text}
                        </Text>
                      )
                    if (item.kind === 'CompactBoundary')
                      return (
                        <Divider
                          key={item.sequence}
                          label={item.text ?? 'Context compacted'}
                          labelPosition="center"
                          color="grape"
                        />
                      )
                    return null
                  })}

                  {turn.ended && (
                    <Group gap={6} c="green.5">
                      <TbCheck size={13} />
                      <Text size="xs">done ({turn.ended.stopReason ?? 'end_turn'})</Text>
                    </Group>
                  )}
                </Stack>
              </Paper>
            ))}
          </Stack>
        )}
      </ScrollArea>
    </Stack>
  )
}
