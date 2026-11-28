import '@xterm/xterm/css/xterm.css'
import { Box, Button, Stack, Text } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { FitAddon } from '@xterm/addon-fit'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'
import { getSessionBuffer, resizeSession, sendSessionInput } from '../../api/sessions'
import { TerminalKeypad } from './TerminalKeypad'
import { createTerminalCopyKeyHandler } from './terminalCopy'
import { createMirrorTerminal } from './terminalMirror'

interface SessionTerminalProps {
  session: AgentSessionSummaryDto
  fill?: boolean
}

interface AgentTextDeltaPayload {
  sessionId: string
  sequence: number
  text: string
}

const HUB_URL = '/hubs/antiphon'

export function SessionTerminal({ session, fill = false }: SessionTerminalProps) {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const sessionId = session.id
  const generation = session.startedAt
  const running = session.status === 'Running'
  const sendInput = useRef<((input: string) => void) | null>(null)
  const [terminalConnection, setTerminalConnection] = useState<{
    sessionId: string; generation: string; joined: boolean; detail?: string; retry?: () => void
  }>()
  const retryAttachment = terminalConnection?.sessionId === sessionId && terminalConnection.generation === generation
    ? terminalConnection.retry : undefined
  const inputEnabled = running && terminalConnection?.sessionId === sessionId
    && terminalConnection.generation === generation && terminalConnection.joined
  // Touch devices / narrow viewports can't produce TUI keys (arrows, esc, tab, ctrl) — give them buttons.
  const showKeypad = useMediaQuery('(max-width: 48em), (pointer: coarse)')

  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    let disposed = false
    let joined = false
    let joinAttempt = 0
    let backlogLoaded = false
    let lastAppliedSequence = 0
    const pendingDeltas: AgentTextDeltaPayload[] = []
    const groupName = `session-${sessionId}`
    const terminal = createMirrorTerminal({ disableStdin: true })
    const fitAddon = new FitAddon()
    terminal.loadAddon(fitAddon)
    terminal.open(host)
    terminal.attachCustomKeyEventHandler(createTerminalCopyKeyHandler(terminal))
    fitAddon.fit()

    const dataDisposable = terminal.onData((input) => {
      if (!disposed) sendInput.current?.(input)
    })
    const resizeDisposable = terminal.onResize(({ cols, rows }) => {
      if (cols > 0 && rows > 0) {
        void resizeSession(sessionId, cols, rows)
      }
    })

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    const setJoined = (acknowledged: boolean, detail?: string, retry?: () => void) => {
      if (disposed) return
      joined = acknowledged
      terminal.options.disableStdin = !running || !joined
      sendInput.current = running && joined ? (input) => {
        if (disposed || connection.state !== HubConnectionState.Connected) return
        void sendSessionInput(sessionId, input).catch((error: unknown) => {
          if (!disposed) {
            const detail = getApiErrorMessage(error, 'Terminal input could not be sent.')
            setTerminalConnection({ sessionId, generation, joined, detail })
          }
        })
      } : null
      setTerminalConnection({ sessionId, generation, joined, detail, retry })
    }

    const join = async (focusOnSuccess = false) => {
      if (disposed || connection.state !== HubConnectionState.Connected) return
      const attempt = ++joinAttempt
      setJoined(false)
      try {
        await connection.invoke('JoinGroup', groupName)
        if (!disposed && attempt === joinAttempt && connection.state === HubConnectionState.Connected) {
          setJoined(true)
          if (focusOnSuccess) terminal.focus()
        }
      } catch (error) {
        if (attempt === joinAttempt) {
          setJoined(false, error instanceof Error ? error.message : 'Terminal attachment was refused.', () => {
            void (async () => {
              await join(true)
              await replayBacklog(true)
            })()
          })
        }
      }
    }

    const applyDelta = (payload: AgentTextDeltaPayload) => {
      if (payload.sessionId !== sessionId || payload.sequence <= lastAppliedSequence) return

      terminal.write(payload.text)
      lastAppliedSequence = payload.sequence
    }

    const replayBacklog = async (replace: boolean) => {
      let buffer
      try {
        buffer = await getSessionBuffer(sessionId)
      } catch {
        return
      }
      if (disposed) return

      backlogLoaded = true
      if (buffer.buffer && (!replace || buffer.lastSequence > lastAppliedSequence)) {
        if (replace) {
          terminal.clear()
        }
        terminal.write(buffer.buffer)
      }
      lastAppliedSequence = Math.max(lastAppliedSequence, buffer.lastSequence)

      for (const delta of pendingDeltas.sort((a, b) => a.sequence - b.sequence)) {
        applyDelta(delta)
      }
      pendingDeltas.length = 0
    }

    const onDelta = (payload: AgentTextDeltaPayload) => {
      if (payload.sessionId !== sessionId) return

      if (!backlogLoaded) {
        pendingDeltas.push(payload)
        return
      }

      applyDelta(payload)
    }

    connection.on('AgentTextDelta', onDelta)
    connection.onreconnecting(() => {
      joinAttempt++
      setJoined(false)
    })
    connection.onclose(() => {
      joinAttempt++
      setJoined(false, 'Terminal connection closed.')
    })
    connection.onreconnected(() => {
      setJoined(false)
      void (async () => {
        await join()
        await replayBacklog(true)
      })()
    })

    const resizeObserver = new ResizeObserver(() => {
      fitAddon.fit()
      if (terminal.cols > 0 && terminal.rows > 0) {
        void resizeSession(sessionId, terminal.cols, terminal.rows)
      }
    })
    resizeObserver.observe(host)

    void (async () => {
      try {
        await connection.start()
        await join()
      } catch {
        setJoined(false, 'Terminal connection is unavailable.')
      }

      await replayBacklog(false)
    })()

    return () => {
      disposed = true
      sendInput.current = null
      resizeObserver.disconnect()
      dataDisposable.dispose()
      resizeDisposable.dispose()
      connection.off('AgentTextDelta', onDelta)
      if (joined && connection.state === HubConnectionState.Connected) {
        void connection.invoke('LeaveGroup', groupName).finally(() => void connection.stop())
      } else {
        void connection.stop()
      }
      terminal.dispose()
    }
  }, [generation, running, sessionId])

  return (
    <Stack gap="xs" h={fill ? '100%' : undefined} style={{ minHeight: 0 }}>
    <Box
      data-testid="session-terminal"
      h={fill ? undefined : 420}
      bg="#111317"
      style={{
        position: 'relative',
        flex: fill ? 1 : undefined,
        minHeight: fill ? 0 : undefined,
        border: '1px solid var(--mantine-color-dark-4)',
        borderRadius: 6,
        overflow: 'hidden',
      }}
    >
      <Box ref={hostRef} h="100%" />
      {!running && (
        <Box
          data-testid="session-terminal-inactive-overlay"
          style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: 'rgba(17, 19, 23, 0.72)',
            backdropFilter: 'blur(1px)',
            pointerEvents: 'auto',
          }}
        >
          <Stack gap={4} align="center" px="md">
            <Text fw={700} size="sm">Session is not running</Text>
            <Text size="xs" c="dimmed" ta="center">
              Terminal input is disabled for {session.status.toLowerCase()} sessions.
            </Text>
            {session.agentKind === 'ClaudeCode' && (
              <Text size="xs" c="dimmed" style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}>
                {session.id}
              </Text>
            )}
          </Stack>
        </Box>
      )}
    </Box>
      {running && (!inputEnabled || terminalConnection?.detail) && (
        <Text size="xs" c="dimmed" role="status">
          {terminalConnection?.sessionId === sessionId && terminalConnection.detail
            ? terminalConnection.detail : 'Waiting for terminal attachment.'}
        </Text>
      )}
      {running && retryAttachment && (
        <Button size="xs" variant="light" onClick={retryAttachment}>Retry terminal attachment</Button>
      )}
      {showKeypad && inputEnabled && (
        <TerminalKeypad onKey={(sequence) => sendInput.current?.(sequence)} />
      )}
    </Stack>
  )
}
