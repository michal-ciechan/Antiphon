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

const HUB_URL = '/hubs/antiphon'

export interface Turn {
  key: string
  prompt?: TranscriptEntryDto
  title?: string
  items: TranscriptEntryDto[] // thinking / tool calls / assistant text, in order
  ended?: TranscriptEntryDto
}

// Group the flat entry stream into turns. A new turn starts at each user prompt; tool results are
// folded into their originating tool call (matched by toolUseId) at render time.
// Exported for tests.
export function buildTurns(entries: TranscriptEntryDto[]): Turn[] {
  const turns: Turn[] = []
  let current: Turn | null = null

  for (const e of entries) {
    if (e.kind === 'UserPrompt') {
      // The interrupt marker is the END of the running turn (no TurnEnd is ever written for it —
      // mirror of the server's IsWorkingAsync), not a new prompt.
      if (isInterruptPrompt(e) && current && !current.ended) {
        current.ended = e
        continue
      }
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

// Claude writes this as a USER message when a turn is aborted (Esc / rejected tool call); such a
// turn produces NO TurnEnd, so the marker IS its end (mirror of the server's IsWorkingAsync).
const INTERRUPTED_PREFIX = '[Request interrupted'

function isInterruptPrompt(e: TranscriptEntryDto): boolean {
  return e.kind === 'UserPrompt' && (e.text ?? '').trimStart().startsWith(INTERRUPTED_PREFIX)
}

// Local slash-commands (/model, /clear, /status …) write their invocation + output into the JSONL
// as USER messages wrapped in these tags, with NO TurnEnd (no API call happens). Housekeeping, not
// work — counting them as activity showed a phantom permanently-working agent and stranded
// WhenIdle deliveries (live miss 2026-07-31; mirror of the server's IsWorkingAsync).
function isLocalCommandRecord(e: TranscriptEntryDto): boolean {
  if (e.kind !== 'UserPrompt') return false
  const t = (e.text ?? '').trimStart()
  return t.startsWith('<command-name>') || t.startsWith('<local-command-stdout>')
}

// Idle once the latest meaningful entry is a TurnEnd; working while activity outranks the last end.
// CompactBoundary is idle-time housekeeping, not activity (mirror of the server's IsWorkingAsync —
// counting it would show a phantom "working" agent after every compaction). Interrupt markers are
// turn ENDS — counting them as activity showed a phantom "working" agent forever after an interrupt.
// Exported for tests: the exclusion list must stay in lockstep with the server.
export function isWorking(entries: TranscriptEntryDto[]): boolean {
  let lastActivity = 0
  let lastEnd = 0
  for (const e of entries) {
    if (e.kind === 'TurnEnd' || isInterruptPrompt(e)) lastEnd = Math.max(lastEnd, e.sequence)
    else if (e.kind !== 'TurnTitle' && e.kind !== 'CompactBoundary' && !isLocalCommandRecord(e))
      lastActivity = Math.max(lastActivity, e.sequence)
  }
  return lastActivity > lastEnd
}

/** Token totals + wall-clock for one turn, plus the idle gap since the previous turn ended. */
export interface TurnMetrics {
  /** Distinct API calls in the turn (assistant entries grouped by apiCallId). */
  apiCalls: number
  inputTokens: number
  outputTokens: number
  cacheReadTokens: number
  cacheCreationTokens: number
  /** Prompt → turn end (ms); null when timestamps are missing or the turn is still running. */
  durationMs: number | null
  /** Time the session sat idle between the previous turn's end and this prompt (ms). */
  idleBeforeMs: number | null
}

const ts = (e: TranscriptEntryDto | undefined): number | null =>
  e?.timestamp ? Date.parse(e.timestamp) : null

/**
 * Compute per-turn metrics. Entries of one API call share apiCallId and repeat IDENTICAL usage,
 * so usage is counted ONCE per distinct apiCallId — summing per entry would overcount.
 * Exported for tests.
 */
export function computeTurnMetrics(turns: Turn[]): TurnMetrics[] {
  let prevEnd: number | null = null
  return turns.map((turn) => {
    const seen = new Set<string>()
    let input = 0
    let output = 0
    let cacheRead = 0
    let cacheCreate = 0
    const all = [...turn.items, ...(turn.ended ? [turn.ended] : [])]
    for (const e of all) {
      if (!e.apiCallId || seen.has(e.apiCallId)) continue
      seen.add(e.apiCallId)
      input += e.inputTokens ?? 0
      output += e.outputTokens ?? 0
      cacheRead += e.cacheReadTokens ?? 0
      cacheCreate += e.cacheCreationTokens ?? 0
    }

    const start = ts(turn.prompt) ?? ts(all[0])
    const end = ts(turn.ended)
    const durationMs = start != null && end != null && end >= start ? end - start : null
    const idleBeforeMs =
      prevEnd != null && start != null && start >= prevEnd ? start - prevEnd : null
    prevEnd = end

    return {
      apiCalls: seen.size,
      inputTokens: input,
      outputTokens: output,
      cacheReadTokens: cacheRead,
      cacheCreationTokens: cacheCreate,
      durationMs,
      idleBeforeMs,
    }
  })
}

/**
 * Merge incoming transcript entries into the current list. Dedup is by LINE UUID (+kind), never by
 * sequence, and live sequences are REBASED past the loaded max — the mirror of the server's
 * persistence dedup, and for the same reason: live SignalR payloads carry the runner tailer's
 * sequence, which restarts at 1 on every session relaunch / re-tail, while the HTTP backlog
 * carries rebased stored sequences. Sequence-dedup silently dropped every live entry for a session
 * with prior relaunches (live miss 2026-07-30: the full-screen files dock never showed new turns),
 * and without the rebase a colliding live sequence sorts into the middle of history.
 * Returns the new sorted list, or null when nothing was new. Exported for tests.
 */
export function mergeTranscriptEntries(
  prev: TranscriptEntryDto[],
  incoming: TranscriptEntryDto[],
  seen: Set<string>,
  counter: { maxSeq: number },
  rebaseLive: boolean,
): TranscriptEntryDto[] | null {
  const keyOf = (e: TranscriptEntryDto) => (e.uuid ? `${e.uuid}:${e.kind}` : `seq:${e.sequence}`)
  const fresh: TranscriptEntryDto[] = []
  for (const e of incoming) {
    const key = keyOf(e)
    if (seen.has(key)) continue
    seen.add(key)
    const seq = rebaseLive && e.sequence <= counter.maxSeq ? counter.maxSeq + 1 : e.sequence
    counter.maxSeq = Math.max(counter.maxSeq, seq)
    fresh.push(seq === e.sequence ? e : { ...e, sequence: seq })
  }
  if (fresh.length === 0) return null
  return [...prev, ...fresh].sort((a, b) => a.sequence - b.sequence)
}

/** "850ms" / "12.4s" / "1m 05s" / "1h 02m". Exported for tests. */
export function formatDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)}s`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ${String(Math.round(s % 60)).padStart(2, '0')}s`
  return `${Math.floor(m / 60)}h ${String(m % 60).padStart(2, '0')}m`
}

/** "412" / "1.2k" / "3.4M". Exported for tests. */
export function formatTokens(n: number): string {
  if (n < 1000) return String(n)
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`
  return `${(n / 1_000_000).toFixed(2)}M`
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
  fitHeight = false,
  initialEntries = null,
}: {
  sessionId: string
  /** Show a message-entry composer at the bottom (send now / queue when idle / raw keystrokes). */
  withComposer?: boolean
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

      {/* Status lives at the BOTTOM — right where the newest turn lands and the composer sits. */}
      <Group gap="xs">
        <Badge
          color={working ? 'yellow' : 'green'}
          variant="light"
          leftSection={working ? <Loader size={10} color="yellow" type="dots" /> : undefined}
        >
          {working ? 'Working…' : 'Idle'}
        </Badge>
      </Group>

      {withComposer && <SmartComposer sessionId={sessionId} defaultMode="send-now" />}
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
