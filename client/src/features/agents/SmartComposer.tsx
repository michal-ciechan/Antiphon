import { useEffect, useMemo, useRef, useState } from 'react'
import {
  Button,
  Combobox,
  Group,
  Mark,
  SegmentedControl,
  Stack,
  Text,
  Textarea,
  Tooltip,
  useCombobox,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbClock, TbKeyboard, TbSend } from 'react-icons/tb'
import {
  enqueueSessionMessage,
  sendSessionInput,
  useSessionCommands,
  type SlashCommandDto,
} from '../../api/sessions'
import { getApiErrorMessage } from '../../api/client'
import { matchRanges } from './matchRanges'

export type ComposerMode = 'raw' | 'send-now' | 'queue'

interface SmartComposerProps {
  sessionId: string
  defaultMode?: ComposerMode
  variant?: 'terminal' | 'messages'
}

const MODES: Record<ComposerMode, { icon: typeof TbSend; label: string; hint: string }> = {
  raw: {
    icon: TbKeyboard,
    label: 'Type into terminal',
    hint: 'Sent straight into the terminal as keystrokes, like typing there yourself.',
  },
  'send-now': {
    icon: TbSend,
    label: 'Send now',
    hint: 'Delivered immediately as a message, even mid-task.',
  },
  queue: {
    icon: TbClock,
    label: 'Queue when idle',
    hint: 'Held until the agent finishes its current turn, then delivered automatically.',
  },
}

/**
 * The agent's primary native input box — ordinary multiline text by default (instant local feedback,
 * no per-keystroke round-trip to the PTY), with `/` autocomplete of available slash-commands + skills,
 * and a per-message dispatch choice (type raw into the terminal / send now / queue when idle).
 *
 * The composer only authors text — Claude itself executes any slash command. It owns no SignalR or
 * pending list; `send-now`/`queue` enqueues surface in the Messages tab via the server's push.
 */
export function SmartComposer({ sessionId, defaultMode = 'send-now', variant = 'messages' }: SmartComposerProps) {
  const [value, setValue] = useState('')
  const [mode, setMode] = useState<ComposerMode>(defaultMode)
  const [busy, setBusy] = useState(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const combobox = useCombobox()

  // The leading token is a slash command only while it starts with "/" and has no whitespace yet
  // (a space means the user is writing arguments → stop suggesting).
  const slashToken = useMemo(() => {
    if (!value.startsWith('/') || /\s/.test(value)) return null
    return value
  }, [value])

  const { data: commands } = useSessionCommands(sessionId, slashToken != null)

  const suggestions = useMemo(() => {
    if (slashToken == null || !commands) return []
    return rankCommands(slashToken, commands)
  }, [slashToken, commands])

  useEffect(() => {
    if (suggestions.length > 0) {
      combobox.openDropdown()
      combobox.resetSelectedOption()
    } else {
      combobox.closeDropdown()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [suggestions.length, slashToken])

  // future: attachments (image paste) / mic input extend this single dispatch seam.
  const dispatch = async (text: string, target: ComposerMode) => {
    setBusy(true)
    try {
      if (target === 'raw') {
        await sendSessionInput(sessionId, text + '\r')
      } else {
        await enqueueSessionMessage(sessionId, text, target === 'send-now' ? 'Now' : 'WhenIdle')
      }
      setValue('')
      combobox.closeDropdown()
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not send the message') })
    } finally {
      setBusy(false)
    }
  }

  const submit = () => {
    const text = value.trim()
    if (text) void dispatch(text, mode)
  }

  const acceptSuggestion = (name: string) => {
    setValue(name + ' ')
    combobox.closeDropdown()
    textareaRef.current?.focus()
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    const autocompleteOpen = combobox.dropdownOpened && suggestions.length > 0
    if (autocompleteOpen) {
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        combobox.selectNextOption()
        return
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault()
        combobox.selectPreviousOption()
        return
      }
      if (e.key === 'Escape') {
        e.preventDefault()
        combobox.closeDropdown()
        return
      }
      if (e.key === 'Tab' || (e.key === 'Enter' && !e.shiftKey)) {
        // Take the highlighted suggestion; Combobox.onOptionSubmit does the insert.
        e.preventDefault()
        combobox.clickSelectedOption()
        return
      }
    }

    // Enter submits (Shift+Enter = newline); Ctrl/⌘+Enter always submits.
    if (e.key === 'Enter' && (!e.shiftKey || e.metaKey || e.ctrlKey)) {
      e.preventDefault()
      submit()
    }
  }

  const active = MODES[mode]
  const ActiveIcon = active.icon

  return (
    <Stack gap={6}>
      <Combobox store={combobox} onOptionSubmit={acceptSuggestion} withinPortal={false}>
        <Combobox.Target>
          <Textarea
            ref={textareaRef}
            placeholder={
              variant === 'terminal'
                ? 'Message the agent or type / for commands…'
                : 'Message to the agent — type / for commands…'
            }
            autosize
            minRows={variant === 'terminal' ? 1 : 2}
            maxRows={6}
            value={value}
            onChange={(e) => setValue(e.currentTarget.value)}
            onKeyDown={handleKeyDown}
          />
        </Combobox.Target>
        <Combobox.Dropdown hidden={suggestions.length === 0}>
          <Combobox.Options mah={260} style={{ overflowY: 'auto' }}>
            {suggestions.map((cmd) => (
              <Combobox.Option value={cmd.name} key={`${cmd.scope}:${cmd.source}:${cmd.name}`}>
                <Group gap="xs" wrap="nowrap" justify="space-between">
                  <div style={{ minWidth: 0 }}>
                    <Text size="sm" fw={600}>
                      <Highlighted text={cmd.name} query={slashToken ?? ''} />
                    </Text>
                    {cmd.description && (
                      <Text size="xs" c="dimmed" lineClamp={1}>
                        {cmd.description}
                      </Text>
                    )}
                  </div>
                  <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                    {cmd.source === 'builtin' ? 'built-in' : `${cmd.source} · ${cmd.scope}`}
                  </Text>
                </Group>
              </Combobox.Option>
            ))}
          </Combobox.Options>
        </Combobox.Dropdown>
      </Combobox>

      <Group justify="space-between" gap="xs">
        <SegmentedControl
          size="xs"
          value={mode}
          onChange={(v) => setMode(v as ComposerMode)}
          data={(Object.keys(MODES) as ComposerMode[]).map((m) => {
            const Icon = MODES[m].icon
            return {
              value: m,
              label: (
                <Tooltip label={MODES[m].label} withArrow>
                  <Icon size={15} aria-label={MODES[m].label} />
                </Tooltip>
              ),
            }
          })}
        />
        <Button
          size="xs"
          leftSection={<ActiveIcon size={14} />}
          loading={busy}
          disabled={!value.trim()}
          onClick={submit}
        >
          {active.label}
        </Button>
      </Group>
      <Text size="xs" c="dimmed">
        {active.hint} Enter to send, Shift+Enter for a newline.
      </Text>
    </Stack>
  )
}

// Rank slash-command suggestions against the typed token (incl. leading "/"): keep only matches,
// prefer earlier match starts, then shorter names, then alphabetical.
function rankCommands(token: string, commands: SlashCommandDto[]): SlashCommandDto[] {
  return commands
    .map((cmd) => ({ cmd, ranges: matchRanges(token, cmd.name) }))
    .filter((x) => x.ranges.length > 0)
    .sort((a, b) => {
      const aStart = a.ranges[0].start
      const bStart = b.ranges[0].start
      if (aStart !== bStart) return aStart - bStart
      if (a.cmd.name.length !== b.cmd.name.length) return a.cmd.name.length - b.cmd.name.length
      return a.cmd.name.localeCompare(b.cmd.name)
    })
    .map((x) => x.cmd)
}

function Highlighted({ text, query }: { text: string; query: string }) {
  const ranges = matchRanges(query, text)
  if (ranges.length === 0) return <span>{text}</span>
  const parts: React.ReactNode[] = []
  let cursor = 0
  for (const [i, range] of ranges.entries()) {
    if (range.start > cursor) parts.push(text.slice(cursor, range.start))
    parts.push(<Mark key={i}>{text.slice(range.start, range.start + range.length)}</Mark>)
    cursor = range.start + range.length
  }
  if (cursor < text.length) parts.push(text.slice(cursor))
  return <span>{parts}</span>
}
