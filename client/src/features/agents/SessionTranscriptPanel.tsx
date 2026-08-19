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
  TbArrowBarDown,
  TbArrowBarUp,
  TbBrain,
  TbCheck,
  TbChevronRight,
  TbClock,
  TbCopy,
  TbDatabase,
  TbExclamationCircle,
  TbHourglass,
  TbTool,
  TbUser,
} from 'react-icons/tb'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import {
  getSessionTranscript,
  type SessionTranscriptPayload,
  type TranscriptEntryDto,
} from '../../api/sessions'
import { SmartComposer } from './SmartComposer'
import {
  buildTurns,
  computeTurnMetrics,
  formatDuration,
  formatTokens,
  isInterruptPrompt,
  isWorking,
  mergeTranscriptEntries,
  ts,
  type Turn,
  type TurnMetrics,
} from './transcriptModel'

const HUB_URL = '/hubs/antiphon'


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
  // Wall-clock of the call: tool_use written → tool_result written (includes any permission wait).
  const callTs = ts(call)
  const resultTs = ts(result)
  const durationMs = callTs != null && resultTs != null && resultTs >= callTs ? resultTs - callTs : null
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
          <Text size="xs" c="dimmed" lineClamp={1} style={{ flexGrow: 1 }}>
            {summarizeToolInput(call.toolInput)}
          </Text>
          {durationMs != null && (
            <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
              {formatDuration(durationMs)}
            </Text>
          )}
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

export function SessionTranscriptPanel({
  sessionId,
  withComposer = false,
  composerCollapsed = false,
  fitHeight = false,
  initialEntries = null,
}: {
  sessionId: string
  /** Show a message-entry composer at the bottom (send now / queue when idle / raw keystrokes). */
  withComposer?: boolean
  /** Composer starts as a single action row — the textbox only appears when pressed. */
  composerCollapsed?: boolean
  /** Fill the parent's height (flex) instead of the fixed embedded height. */
  fitHeight?: boolean
  /** Storybook/screenshot hook: render these entries statically — no HTTP fetch, no SignalR. */
  initialEntries?: TranscriptEntryDto[] | null
}) {
  const [entries, setEntries] = useState<TranscriptEntryDto[]>(initialEntries ?? [])
  const [loading, setLoading] = useState(initialEntries == null)
  // Merge bookkeeping lives in refs (NOT inside a setEntries updater — StrictMode double-invokes
  // updaters, and a seen-set mutated twice drops every entry on the second pass).
  const entriesRef = useRef<TranscriptEntryDto[]>(initialEntries ?? [])
  const seenRef = useRef<Set<string>>(new Set())
  const counterRef = useRef({ maxSeq: 0 })
  const viewportRef = useRef<HTMLDivElement>(null)
  const didInitialScroll = useRef(false)
  // Whether the user is (still) reading the live edge. Updated from SCROLL events, not measured
  // when entries arrive — by then the new content has already grown scrollHeight and the "am I
  // near the bottom?" answer is always no (live miss 2026-07-30: the view stopped following the
  // stream the moment a turn taller than the threshold landed).
  const stickToBottom = useRef(true)

  useEffect(() => {
    if (initialEntries != null) return
    let disposed = false
    entriesRef.current = []
    seenRef.current = new Set()
    counterRef.current = { maxSeq: 0 }
    didInitialScroll.current = false
    stickToBottom.current = true
    setEntries([])
    setLoading(true)

    const merge = (incoming: TranscriptEntryDto[], rebaseLive: boolean) => {
      const next = mergeTranscriptEntries(
        entriesRef.current, incoming, seenRef.current, counterRef.current, rebaseLive)
      if (next) {
        entriesRef.current = next
        setEntries(next)
      }
    }

    const load = async () => {
      try {
        const data = await getSessionTranscript(sessionId)
        if (!disposed) merge(data.entries, false)
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
      if (payload.sessionId === sessionId) merge([payload], true)
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
  }, [sessionId, initialEntries])

  // The newest activity lives at the bottom: jump there once the backlog first renders, then keep
  // following the stream while the user was reading the live edge (never fight a deliberate
  // scroll back through history).
  useEffect(() => {
    const viewport = viewportRef.current
    if (!viewport || loading || entries.length === 0) return
    const firstLoad = !didInitialScroll.current
    didInitialScroll.current = true
    if (firstLoad || stickToBottom.current)
      requestAnimationFrame(() => viewport.scrollTo({ top: viewport.scrollHeight }))
  }, [loading, entries.length])

  const turns = useMemo(() => buildTurns(entries), [entries])
  const metrics = useMemo(() => computeTurnMetrics(turns), [turns])
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

  const sessionTotals = useMemo(() => {
    let input = 0
    let output = 0
    let cache = 0
    for (const m of metrics) {
      input += m.inputTokens
      output += m.outputTokens
      cache += m.cacheReadTokens + m.cacheCreationTokens
    }
    return { input, output, cache }
  }, [metrics])

  const statusBadge = (
    <Badge
      color={working ? 'yellow' : 'green'}
      variant="light"
      leftSection={working ? <Loader size={10} color="yellow" type="dots" /> : undefined}
      style={{ flexShrink: 0 }}
    >
      {working ? 'Working…' : 'Idle'}
    </Badge>
  )

  return (
    <Stack gap="xs" style={fitHeight ? { minHeight: 0, height: '100%', flexGrow: 1 } : { minHeight: 0 }}>
      <Group justify="space-between">
        <Group gap="xs">
          <Text size="xs" c="dimmed">
            {turns.length} turn{turns.length === 1 ? '' : 's'}
          </Text>
          {sessionTotals.output > 0 && (
            <Tooltip label="Session totals — input / output / cache (read + creation) tokens">
              <Text size="xs" c="dimmed">
                ↓{formatTokens(sessionTotals.input)} ↑{formatTokens(sessionTotals.output)} ⛁
                {formatTokens(sessionTotals.cache)}
              </Text>
            </Tooltip>
          )}
        </Group>
        <Tooltip label="Copy the latest answer (including any that scrolled off the terminal)">
          <ActionIcon variant="subtle" disabled={!latestAnswer} onClick={copyAnswer} aria-label="Copy final output">
            <TbCopy size={16} />
          </ActionIcon>
        </Tooltip>
      </Group>

      <ScrollArea
        h={fitHeight ? undefined : 460}
        type="auto"
        offsetScrollbars
        viewportRef={viewportRef}
        onScrollPositionChange={() => {
          const viewport = viewportRef.current
          if (viewport)
            stickToBottom.current =
              viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight < 120
        }}
        style={fitHeight ? { flexGrow: 1, minHeight: 0 } : undefined}
      >
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
            {turns.map((turn, i) => (
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

                  <TurnFooter turn={turn} metrics={metrics[i]} />
                </Stack>
              </Paper>
            ))}
          </Stack>
        )}
      </ScrollArea>

      {/* Status lives at the BOTTOM, where the newest turn lands. With a composer it joins the
          composer's action row (below the textbox) instead of spending a row of its own. */}
      {withComposer ? (
        <SmartComposer
          sessionId={sessionId}
          defaultMode="send-now"
          collapsible={composerCollapsed}
          actions={statusBadge}
        />
      ) : (
        <Group gap="xs">{statusBadge}</Group>
      )}
    </Stack>
  )
}

function TurnFooter({ turn, metrics }: { turn: Turn; metrics: TurnMetrics | undefined }) {
  const interrupted = turn.ended != null && isInterruptPrompt(turn.ended)
  const hasMetrics =
    metrics != null && (metrics.apiCalls > 0 || metrics.durationMs != null || metrics.idleBeforeMs != null)
  if (!turn.ended && !hasMetrics) return null

  return (
    <Group gap="md" wrap="wrap">
      {turn.ended &&
        (interrupted ? (
          <Group gap={6} c="orange.5">
            <TbExclamationCircle size={13} />
            <Text size="xs">interrupted</Text>
          </Group>
        ) : (
          <Group gap={6} c="green.5">
            <TbCheck size={13} />
            <Text size="xs">done ({turn.ended.stopReason ?? 'end_turn'})</Text>
          </Group>
        ))}
      {metrics && metrics.durationMs != null && (
        <Tooltip label="Wall-clock time from your prompt to the turn's end">
          <Group gap={4} c="dimmed">
            <TbClock size={12} />
            <Text size="xs">{formatDuration(metrics.durationMs)}</Text>
          </Group>
        </Tooltip>
      )}
      {metrics && metrics.idleBeforeMs != null && metrics.idleBeforeMs >= 1000 && (
        <Tooltip label="Idle time between the previous turn's end and this prompt">
          <Group gap={4} c="dimmed">
            <TbHourglass size={12} />
            <Text size="xs">idle {formatDuration(metrics.idleBeforeMs)}</Text>
          </Group>
        </Tooltip>
      )}
      {metrics && metrics.apiCalls > 0 && (
        <Tooltip
          label={`${metrics.apiCalls} API call${metrics.apiCalls === 1 ? '' : 's'} — input / output / cache-read / cache-write tokens`}
        >
          <Group gap={6} c="dimmed" wrap="nowrap">
            <Text size="xs">{metrics.apiCalls}×</Text>
            <Group gap={2} wrap="nowrap">
              <TbArrowBarDown size={12} />
              <Text size="xs">{formatTokens(metrics.inputTokens)}</Text>
            </Group>
            <Group gap={2} wrap="nowrap">
              <TbArrowBarUp size={12} />
              <Text size="xs">{formatTokens(metrics.outputTokens)}</Text>
            </Group>
            <Group gap={2} wrap="nowrap">
              <TbDatabase size={12} />
              <Text size="xs">
                {formatTokens(metrics.cacheReadTokens)}
                {metrics.cacheCreationTokens > 0 ? `+${formatTokens(metrics.cacheCreationTokens)}` : ''}
              </Text>
            </Group>
          </Group>
        </Tooltip>
      )}
    </Group>
  )
}
