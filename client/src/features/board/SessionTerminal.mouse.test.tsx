import { Terminal, type ITerminalOptions } from '@xterm/xterm'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { getSessionBuffer, resizeSession, sendSessionInput } from '../../api/sessions'
import { renderWithProviders, waitFor } from '../../test/utils'
import { SessionTerminal } from './SessionTerminal'
import * as mirrorFactory from './terminalMirror'
import { GROK_STARTUP } from './__fixtures__/grok-startup'

vi.mock('../../api/sessions', () => ({
  getSessionBuffer: vi.fn(),
  resizeSession: vi.fn(async () => undefined),
  sendSessionInput: vi.fn(async () => undefined),
}))
vi.mock('@xterm/addon-fit', () => ({
  FitAddon: class { activate() {} fit() {} dispose() {} },
}))
const connection = vi.hoisted(() => ({
  state: 'Disconnected',
  handlers: new Map<string, (payload: unknown) => void>(),
  reconnected: undefined as undefined | (() => void),
  start: vi.fn(async function () { connection.state = 'Connected' }),
  stop: vi.fn(async function () { connection.state = 'Disconnected' }),
  invoke: vi.fn(async () => undefined),
  on: (event: string, handler: (payload: unknown) => void) => connection.handlers.set(event, handler),
  off: (event: string) => connection.handlers.delete(event),
  onreconnected: (handler: () => void) => { connection.reconnected = handler },
}))
vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected' },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return connection }
  },
}))

const runningSession: AgentSessionSummaryDto = {
  id: 'session-1', definitionName: 'grok', agentKind: 'Grok', status: 'Running',
  cwd: '/test', createdAt: '2026-01-01T00:00:00Z', startedAt: '2026-01-01T00:00:00Z',
  lastSeenAt: '2026-01-01T00:00:01Z', endedAt: null, exitCode: null, failureReason: null,
}
const layout: ITerminalOptions = {
  cols: 20, rows: 6, lineHeight: 1, letterSpacing: 0,
  smoothScrollDuration: 0, allowProposedApi: true,
}
const realFactory = mirrorFactory.createMirrorTerminal
const owned: Terminal[] = []
const hosts: HTMLElement[] = []
const restoreProperties: Array<() => void> = []
let panel: Terminal
let panelWrite: ReturnType<typeof vi.spyOn<Terminal, 'write'>>

function replaceProperty(target: object, name: string, descriptor: PropertyDescriptor) {
  const original = Object.getOwnPropertyDescriptor(target, name)
  Object.defineProperty(target, name, { configurable: true, ...descriptor })
  restoreProperties.push(() => {
    if (original) Object.defineProperty(target, name, original)
    else Reflect.deleteProperty(target, name)
  })
}

// Only layout is supplied: xterm's real measurement, selection and viewport services run.
function installGeometry() {
  replaceProperty(window, 'devicePixelRatio', { value: 1 })
  for (const [name, dimension] of [['offsetWidth', 'width'], ['offsetHeight', 'height'],
    ['clientWidth', 'width'], ['clientHeight', 'height']] as const) {
    const original = Object.getOwnPropertyDescriptor(HTMLElement.prototype, name)?.get
    replaceProperty(HTMLElement.prototype, name, { get(this: HTMLElement) {
      if (this.classList.contains('xterm-char-measure-element')) {
        return dimension === 'width' ? (this.textContent?.length ?? 0) * 10 : 20
      }
      if (this.closest('.xterm') || this.querySelector('.xterm')) return dimension === 'width' ? 200 : 120
      return original?.call(this) ?? 0
    } })
  }
  const originalRect = HTMLElement.prototype.getBoundingClientRect
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this.closest('.xterm') || this.querySelector('.xterm')) return new DOMRect(0, 0, 200, 120)
    return originalRect.call(this)
  })
  const style = document.createElement('style')
  style.textContent = '.xterm, .xterm-screen { padding: 0; }'
  document.head.append(style)
  restoreProperties.push(() => style.remove())
}

type MeasuredTerminal = { _core: { _renderService: { dimensions: {
  css: { cell: { width: number; height: number }; canvas: { width: number; height: number } }
  device: { cell: { width: number; height: number } }
} } } }
function assertGeometry(terminal: Terminal) {
  const dimensions = (terminal as unknown as MeasuredTerminal)._core._renderService.dimensions
  expect(dimensions.css.cell, 'harness CSS cell').toEqual({ width: 10, height: 20 })
  expect(dimensions.css.canvas, 'harness canvas').toEqual({ width: 200, height: 120 })
  expect(dimensions.device.cell, 'harness device cell').toEqual({ width: 10, height: 20 })
}
function openTerminal(mirror: boolean, options: ITerminalOptions = {}) {
  const terminal = mirror ? realFactory({ ...layout, ...options }) : new Terminal({ ...layout, ...options })
  owned.push(terminal)
  const host = document.createElement('div')
  hosts.push(host)
  document.body.append(host)
  terminal.open(host)
  assertGeometry(terminal)
  return terminal
}
function collect(terminal: Terminal) {
  const data: string[] = []
  const binary: string[] = []
  terminal.onData(value => data.push(value))
  terminal.onBinary(value => binary.push(value))
  return { data, binary, clear: () => { data.length = 0; binary.length = 0 } }
}
function write(terminal: Terminal, text: string) {
  return new Promise<void>(resolve => terminal.write(text, resolve))
}
function screen(terminal: Terminal) {
  return terminal.element!.querySelector<HTMLElement>('.xterm-screen')!
}
function hover(terminal: Terminal) {
  for (const clientX of [45, 55]) screen(terminal).dispatchEvent(new MouseEvent('mousemove', {
    bubbles: true, cancelable: true, clientX, clientY: 50, buttons: 0,
  }))
}
async function panelParsed(text: string) {
  await waitFor(() => expect(panelWrite).toHaveBeenCalledWith(text))
  await write(panel, '') // FIFO parser barrier after observing the component's actual write.
  assertGeometry(panel)
}

describe.sequential('CARD-0148 mouse policy', () => {
  beforeEach(() => {
    installGeometry()
    vi.mocked(sendSessionInput).mockClear()
    vi.mocked(resizeSession).mockClear()
    vi.mocked(getSessionBuffer).mockReset().mockResolvedValue({
      sessionId: 'session-1', buffer: GROK_STARTUP, lastSequence: 1,
    })
    connection.state = 'Disconnected'
    connection.handlers.clear()
    connection.reconnected = undefined
    connection.invoke.mockClear()
    vi.spyOn(mirrorFactory, 'createMirrorTerminal').mockImplementation(options => {
      panel = realFactory({ ...options, ...layout })
      panelWrite = vi.spyOn(panel, 'write')
      return panel
    })
  })

  afterEach(() => {
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0, buttons: 0 }))
    cleanup()
    owned.splice(0).forEach(terminal => terminal.dispose())
    hosts.splice(0).forEach(host => host.remove())
    restoreProperties.splice(0).reverse().forEach(restore => restore())
    vi.restoreAllMocks()
  })

  it('panel rejects hover from backlog', async () => {
    const bare = openTerminal(false)
    const emitted = collect(bare)
    await write(bare, GROK_STARTUP)
    emitted.clear()
    hover(bare)
    expect(emitted.data, 'bare reachability').toEqual(['\x1b[<35;5;3M', '\x1b[<35;6;3M'])
    expect(emitted.binary).toEqual([])

    renderWithProviders(<SessionTerminal session={runningSession} />)
    await panelParsed(GROK_STARTUP)
    vi.mocked(sendSessionInput).mockClear()
    hover(panel)
    expect(sendSessionInput, 'panel transport silence').not.toHaveBeenCalled()
    panel.input('x')
    expect(sendSessionInput).toHaveBeenCalledExactlyOnceWith('session-1', 'x')
  })
})
