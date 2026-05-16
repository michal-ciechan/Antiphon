import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { SessionTerminal } from './SessionTerminal'

const xtermMock = vi.hoisted(() => ({
  instances: [] as Array<{
    write: ReturnType<typeof vi.fn>
    dispose: ReturnType<typeof vi.fn>
    onDataHandler?: (input: string) => void
    onResizeHandler?: (size: { cols: number; rows: number }) => void
    cols: number
    rows: number
  }>,
  fit: vi.fn(),
}))

const signalrMock = vi.hoisted(() => ({
  connection: {
    state: 'Disconnected',
    handlers: new Map<string, (payload: unknown) => void>(),
    reconnected: undefined as undefined | (() => void),
    start: vi.fn(async function (this: { state: string }) {
      this.state = 'Connected'
    }),
    stop: vi.fn(async function (this: { state: string }) {
      this.state = 'Disconnected'
    }),
    invoke: vi.fn(async () => undefined),
    on: vi.fn(function (this: { handlers: Map<string, (payload: unknown) => void> }, event: string, handler: (payload: unknown) => void) {
      this.handlers.set(event, handler)
    }),
    off: vi.fn(function (this: { handlers: Map<string, (payload: unknown) => void> }, event: string) {
      this.handlers.delete(event)
    }),
    onreconnected: vi.fn(function (this: { reconnected?: () => void }, handler: () => void) {
      this.reconnected = handler
    }),
  },
}))

vi.mock('@xterm/xterm', () => ({
  Terminal: class {
    write = vi.fn()
    dispose = vi.fn()
    cols = 100
    rows = 30
    onDataHandler?: (input: string) => void
    onResizeHandler?: (size: { cols: number; rows: number }) => void

    constructor() {
      xtermMock.instances.push(this)
    }

    loadAddon() {}
    open() {}
    onData(handler: (input: string) => void) {
      this.onDataHandler = handler
      return { dispose: vi.fn() }
    }
    onResize(handler: (size: { cols: number; rows: number }) => void) {
      this.onResizeHandler = handler
      return { dispose: vi.fn() }
    }
  },
}))

vi.mock('@xterm/addon-fit', () => ({
  FitAddon: class {
    fit = xtermMock.fit
  },
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: {
    Connected: 'Connected',
    Disconnected: 'Disconnected',
  },
  LogLevel: {
    Warning: 3,
  },
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return signalrMock.connection }
  },
}))

describe('SessionTerminal', () => {
  beforeEach(() => {
    xtermMock.instances.length = 0
    xtermMock.fit.mockClear()
    signalrMock.connection.state = 'Disconnected'
    signalrMock.connection.handlers.clear()
    signalrMock.connection.reconnected = undefined
    signalrMock.connection.start.mockClear()
    signalrMock.connection.stop.mockClear()
    signalrMock.connection.invoke.mockClear()
    signalrMock.connection.on.mockClear()
    signalrMock.connection.off.mockClear()
    signalrMock.connection.onreconnected.mockClear()
  })

  it('writes backlog, appends matching live deltas, sends input, resizes, and leaves the group', async () => {
    const inputSpy = vi.fn()
    const resizeSpy = vi.fn()
    server.use(
      http.get('/api/sessions/session-1/buffer', () =>
        HttpResponse.json({ sessionId: 'session-1', buffer: 'BACKLOG', lastSequence: 1 }),
      ),
      http.post('/api/sessions/session-1/input', async ({ request }) => {
        inputSpy(await request.json())
        return new HttpResponse(null, { status: 204 })
      }),
      http.post('/api/sessions/session-1/resize', async ({ request }) => {
        resizeSpy(await request.json())
        return new HttpResponse(null, { status: 204 })
      }),
    )

    const { unmount } = renderWithProviders(<SessionTerminal sessionId="session-1" />)

    await waitFor(() => {
      expect(signalrMock.connection.invoke).toHaveBeenCalledWith('JoinGroup', 'session-session-1')
      expect(xtermMock.instances[0].write).toHaveBeenCalledWith('BACKLOG')
    })

    signalrMock.connection.handlers.get('AgentTextDelta')?.({
      sessionId: 'session-2',
      sequence: 1,
      text: 'NOPE',
    })
    signalrMock.connection.handlers.get('AgentTextDelta')?.({
      sessionId: 'session-1',
      sequence: 2,
      text: 'LIVE',
    })

    expect(xtermMock.instances[0].write).not.toHaveBeenCalledWith('NOPE')
    expect(xtermMock.instances[0].write).toHaveBeenCalledWith('LIVE')

    xtermMock.instances[0].onDataHandler?.('x')
    xtermMock.instances[0].onResizeHandler?.({ cols: 90, rows: 25 })

    await waitFor(() => expect(inputSpy).toHaveBeenCalledWith({ input: 'x' }))
    await waitFor(() => expect(resizeSpy).toHaveBeenCalledWith({ cols: 90, rows: 25 }))

    signalrMock.connection.reconnected?.()
    await waitFor(() => {
      expect(signalrMock.connection.invoke).toHaveBeenCalledWith('JoinGroup', 'session-session-1')
    })

    unmount()

    await waitFor(() => {
      expect(signalrMock.connection.invoke).toHaveBeenCalledWith('LeaveGroup', 'session-session-1')
      expect(signalrMock.connection.stop).toHaveBeenCalled()
      expect(xtermMock.instances[0].dispose).toHaveBeenCalled()
    })
  })

  it('writes the backlog before queued live deltas and drops deltas already included in the snapshot', async () => {
    let releaseBuffer!: () => void
    const bufferReady = new Promise<void>((resolve) => {
      releaseBuffer = resolve
    })
    server.use(
      http.get('/api/sessions/session-1/buffer', async () => {
        await bufferReady
        return HttpResponse.json({
          sessionId: 'session-1',
          buffer: 'BACKLOG_AND_EARLY',
          lastSequence: 1,
        })
      }),
    )

    renderWithProviders(<SessionTerminal sessionId="session-1" />)

    await waitFor(() => {
      expect(signalrMock.connection.invoke).toHaveBeenCalledWith('JoinGroup', 'session-session-1')
    })

    signalrMock.connection.handlers.get('AgentTextDelta')?.({
      sessionId: 'session-1',
      sequence: 1,
      text: 'EARLY',
    })
    signalrMock.connection.handlers.get('AgentTextDelta')?.({
      sessionId: 'session-1',
      sequence: 2,
      text: 'LATE',
    })

    releaseBuffer()

    await waitFor(() => {
      expect(xtermMock.instances[0].write).toHaveBeenNthCalledWith(1, 'BACKLOG_AND_EARLY')
      expect(xtermMock.instances[0].write).toHaveBeenNthCalledWith(2, 'LATE')
    })
    expect(xtermMock.instances[0].write).not.toHaveBeenCalledWith('EARLY')
  })
})
