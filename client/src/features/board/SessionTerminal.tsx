import '@xterm/xterm/css/xterm.css'
import { Box, Stack, Text } from '@mantine/core'
import { useEffect, useRef } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { getSessionBuffer, resizeSession, sendSessionInput } from '../../api/sessions'

interface SessionTerminalProps {
  session: AgentSessionSummaryDto
}

interface AgentTextDeltaPayload {
  sessionId: string
  sequence: number
  text: string
}

const HUB_URL = '/hubs/antiphon'

export function SessionTerminal({ session }: SessionTerminalProps) {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const sessionId = session.id
  const inputEnabled = session.status === 'Running'

  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    let disposed = false
    let joined = false
    let backlogLoaded = false
    let lastAppliedSequence = 0
    const pendingDeltas: AgentTextDeltaPayload[] = []
    const groupName = `session-${sessionId}`
    const terminal = new Terminal({
      cursorBlink: true,
      convertEol: true,
      disableStdin: !inputEnabled,
      fontFamily: 'Cascadia Mono, Consolas, monospace',
      fontSize: 13,
      theme: {
        background: '#111317',
        foreground: '#d9e2ef',
        cursor: '#4dabf7',
        selectionBackground: '#2d72d266',
      },
    })
    const fitAddon = new FitAddon()
    terminal.loadAddon(fitAddon)
    terminal.open(host)
    fitAddon.fit()

    const dataDisposable = terminal.onData((input) => {
      if (!inputEnabled) return
      void sendSessionInput(sessionId, input)
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

    const join = async () => {
      if (disposed || connection.state !== HubConnectionState.Connected) return
      await connection.invoke('JoinGroup', groupName)
      joined = true
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
    connection.onreconnected(() => {
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
        // Backlog remains useful even if the live connection is unavailable.
      }

      await replayBacklog(false)
    })()

    return () => {
      disposed = true
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
  }, [inputEnabled, sessionId])

  return (
    <Box
      data-testid="session-terminal"
      h={420}
      bg="#111317"
      style={{
        position: 'relative',
        border: '1px solid var(--mantine-color-dark-4)',
        borderRadius: 6,
        overflow: 'hidden',
      }}
    >
      <Box ref={hostRef} h="100%" />
      {!inputEnabled && (
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
  )
}
