import '@xterm/xterm/css/xterm.css'
import { Box } from '@mantine/core'
import { useEffect, useRef } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import { getSessionBuffer, resizeSession, sendSessionInput } from '../../api/sessions'

interface SessionTerminalProps {
  sessionId: string
}

interface AgentTextDeltaPayload {
  sessionId: string
  sequence: number
  text: string
}

const HUB_URL = '/hubs/antiphon'

export function SessionTerminal({ sessionId }: SessionTerminalProps) {
  const hostRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    let disposed = false
    let joined = false
    let backlogLoaded = false
    let backlogSequence = 0
    const pendingDeltas: AgentTextDeltaPayload[] = []
    const groupName = `session-${sessionId}`
    const terminal = new Terminal({
      cursorBlink: true,
      convertEol: true,
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

    const onDelta = (payload: AgentTextDeltaPayload) => {
      if (payload.sessionId !== sessionId) return

      if (!backlogLoaded) {
        pendingDeltas.push(payload)
        return
      }

      if (payload.sequence > backlogSequence) {
        terminal.write(payload.text)
      }
    }

    connection.on('AgentTextDelta', onDelta)
    connection.onreconnected(() => {
      void join()
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

      let buffer
      try {
        buffer = await getSessionBuffer(sessionId)
      } catch {
        return
      }
      if (disposed) return

      backlogLoaded = true
      backlogSequence = buffer.lastSequence
      if (buffer.buffer) {
        terminal.write(buffer.buffer)
      }

      for (const delta of pendingDeltas
        .filter((delta) => delta.sequence > backlogSequence)
        .sort((a, b) => a.sequence - b.sequence)) {
        terminal.write(delta.text)
      }
      pendingDeltas.length = 0
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
  }, [sessionId])

  return (
    <Box
      ref={hostRef}
      data-testid="session-terminal"
      h={420}
      bg="#111317"
      style={{
        border: '1px solid var(--mantine-color-dark-4)',
        borderRadius: 6,
        overflow: 'hidden',
      }}
    />
  )
}
