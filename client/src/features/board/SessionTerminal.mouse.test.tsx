import { Terminal, type ITerminalOptions } from '@xterm/xterm'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest'
import type { AgentSessionSummaryDto } from '../../api/boards'
import { getSessionBuffer, resizeSession, sendSessionInput } from '../../api/sessions'
import { renderWithProviders, waitFor } from '../../test/utils'
import { SessionTerminal } from './SessionTerminal'
import * as mirrorFactory from './terminalMirror'
import * as mousePolicy from './terminalMouse'
import { createTerminalCopyKeyHandler } from './terminalCopy'
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
const realMousePolicy = mousePolicy.configureLocalTerminalMouse
const owned: Terminal[] = []
const hosts: HTMLElement[] = []
const restoreProperties: Array<() => void> = []
let panel: Terminal
let panelWrite: MockInstance<Terminal['write']>

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
function closeTerminal(terminal: Terminal) {
  const host = terminal.element!.parentElement!
  terminal.dispose()
  owned.splice(owned.indexOf(terminal), 1)
  hosts.splice(hosts.indexOf(host), 1)
  host.remove()
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

function mouse(target: EventTarget, type: string, clientX: number, clientY: number,
  options: MouseEventInit = {}) {
  target.dispatchEvent(new MouseEvent(type, {
    bubbles: true, cancelable: true, clientX, clientY,
    button: 0, buttons: type === 'mouseup' ? 0 : 1, detail: 1, ...options,
  }))
}
function press(terminal: Terminal, modifiers: MouseEventInit = {}) {
  mouse(screen(terminal), 'mousedown', 45, 50, modifiers)
}
function release(modifiers: MouseEventInit = {}) {
  mouse(document, 'mouseup', 55, 50, modifiers)
}
function dragHello(terminal: Terminal) {
  mouse(screen(terminal), 'mousedown', 1, 10)
  mouse(document, 'mousemove', 51, 10)
  mouse(document, 'mouseup', 51, 10)
}
function wheel(terminal: Terminal, deltaY: number) {
  screen(terminal).dispatchEvent(new WheelEvent('wheel', {
    bubbles: true, cancelable: true, clientX: 45, clientY: 50, deltaMode: 1, deltaY,
  }))
}
function gestures(terminal: Terminal) {
  for (const modifiers of [{}, { ctrlKey: true }, { altKey: true }]) {
    press(terminal, modifiers)
    mouse(document, 'mousemove', 55, 50, modifiers)
    release(modifiers)
  }
  hover(terminal)
  wheel(terminal, -1)
  wheel(terminal, 1)
}
async function framesUntil(terminal: Terminal, condition: () => boolean, label: string) {
  terminal.refresh(0, terminal.rows - 1)
  for (let frame = 0; frame < 10; frame++) {
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    if (condition()) return
  }
  expect(condition(), `harness frame bound: ${label}`).toBe(true)
}
const protocols = [
  { name: 'X10', mode: 9 }, { name: 'VT200', mode: 1000 },
  { name: 'DRAG', mode: 1002 }, { name: 'ANY', mode: 1003 },
]
const encodings = [
  { name: 'DEFAULT', mode: '', data: [] as string[], binary: ['\x1b[M \x25\x23'] },
  { name: 'SGR', mode: ';1006', data: ['\x1b[<0;5;3M'], binary: [] as string[] },
  { name: 'SGR_PIXELS', mode: ';1016', data: ['\x1b[<0;45;50M'], binary: [] as string[] },
]
type DirectMouseEvent = {
  col: number; row: number; x: number; y: number; button: number; action: number
  ctrl: boolean; alt: boolean; shift: boolean
}
type MouseInternals = { _core: { coreMouseService: {
  addProtocol: (name: string, protocol: unknown) => void
  triggerMouseEvent: (event: DirectMouseEvent) => boolean
} } }
function directPress(terminal: Terminal) {
  // A separate defensive-restriction oracle. DOM reachability is proved above it.
  return (terminal as unknown as MouseInternals)._core.coreMouseService.triggerMouseEvent({
    col: 4, row: 2, x: 45, y: 50, button: 0, action: 1, ctrl: false, alt: false, shift: false,
  })
}
function inputControl(terminal: Terminal) {
  const emitted = collect(terminal)
  terminal.input('x')
  expect(emitted.data).toEqual(['x'])
  expect(emitted.binary).toEqual([])
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

  it('panel rejects hover from live and reconnect', async () => {
    let releaseBuffer!: (value: Awaited<ReturnType<typeof getSessionBuffer>>) => void
    vi.mocked(getSessionBuffer).mockReturnValueOnce(new Promise(resolve => { releaseBuffer = resolve }))
    renderWithProviders(<SessionTerminal session={runningSession} />)
    await waitFor(() => expect(getSessionBuffer).toHaveBeenCalledTimes(1))
    connection.handlers.get('AgentTextDelta')!({ sessionId: 'session-1', sequence: 2, text: GROK_STARTUP })
    releaseBuffer({ sessionId: 'session-1', buffer: '', lastSequence: 1 })
    for (const phase of ['queued', 'live', 'reconnect']) {
      if (phase !== 'queued') {
        await write(panel, '\x1b[?1003l')
        panelWrite.mockClear()
        if (phase === 'live') {
          connection.handlers.get('AgentTextDelta')!({ sessionId: 'session-1', sequence: 3, text: GROK_STARTUP })
        } else {
          vi.mocked(getSessionBuffer).mockResolvedValueOnce({
            sessionId: 'session-1', buffer: GROK_STARTUP, lastSequence: 4,
          })
          connection.reconnected!()
        }
      }
      await panelParsed(GROK_STARTUP)
      vi.mocked(sendSessionInput).mockClear()
      hover(panel)
      expect(sendSessionInput, phase).not.toHaveBeenCalled()
      panel.input('x')
      expect(sendSessionInput, phase).toHaveBeenCalledExactlyOnceWith('session-1', 'x')
    }
  })

  it.each(protocols)('protocol $name stays local', async ({ name, mode }) => {
    for (const encoding of encodings) {
      const label = `${name}/${encoding.name}`
      const setup = `\x1b[?${mode}${encoding.mode}h\x1b[HHELLO WORLD`
      const bare = openTerminal(false)
      const bareEvents = collect(bare)
      await write(bare, setup)
      press(bare)
      expect(bareEvents.data, `${label} DOM reachability`).toEqual(encoding.data)
      expect(bareEvents.binary, `${label} DOM reachability`).toEqual(encoding.binary)
      release()
      bareEvents.clear()
      expect(directPress(bare), `${label} direct reachability`).toBe(true)
      expect(bareEvents.data, label).toEqual(encoding.data)
      expect(bareEvents.binary, label).toEqual(encoding.binary)
      closeTerminal(bare)

      const terminal = openTerminal(true)
      const emitted = collect(terminal)
      await write(terminal, setup)
      gestures(terminal)
      expect.soft(emitted.data, `${label} DOM data`).toEqual([])
      expect.soft(emitted.binary, `${label} DOM binary`).toEqual([])
      dragHello(terminal)
      expect.soft(terminal.getSelection(), `${label} ordinary selection`).toBe('HELLO')
      expect.soft(emitted.data, `${label} selection data`).toEqual([])
      expect.soft(emitted.binary, `${label} selection binary`).toEqual([])
      emitted.clear()
      expect.soft(directPress(terminal), `${label} direct restriction`).toBe(false)
      expect.soft(emitted.data, `${label} direct data`).toEqual([])
      expect.soft(emitted.binary, `${label} direct binary`).toEqual([])
      closeTerminal(terminal)
    }
  })

  it('normal drag copies selection', async () => {
    const bare = openTerminal(false)
    await write(bare, 'HELLO WORLD')
    dragHello(bare)
    expect(bare.getSelection(), 'bare ordinary drag').toBe('HELLO')
    closeTerminal(bare)
    const terminal = openTerminal(true)
    const emitted = collect(terminal)
    await write(terminal, GROK_STARTUP + '\x1b[HHELLO WORLD')
    dragHello(terminal)
    expect(terminal.getSelection()).toBe('HELLO')
    expect(emitted.data).toEqual([])
    expect(emitted.binary).toEqual([])
    const writeText = vi.fn(async () => undefined)
    replaceProperty(navigator, 'clipboard', { value: { writeText } })
    const result = createTerminalCopyKeyHandler(terminal)(new KeyboardEvent('keydown', {
      key: 'c', code: 'KeyC', ctrlKey: true,
    }))
    expect(result).toBe(true)
    expect(writeText).toHaveBeenCalledExactlyOnceWith('HELLO')
  })

  it('wheel scrolls local history', async () => {
    for (const mirror of [false, true]) {
      const label = mirror ? 'mirror' : 'bare wheel reachability'
      const terminal = openTerminal(mirror, { scrollback: 100 })
      const emitted = collect(terminal)
      await write(terminal, (mirror ? GROK_STARTUP : '') +
        Array.from({ length: 30 }, (_, index) => `line ${index}`).join('\r\n'))
      await framesUntil(terminal, () => terminal.buffer.active.baseY > 10, `${label} history`)
      terminal.scrollToLine(10)
      await framesUntil(terminal, () => terminal.buffer.active.viewportY === 10, `${label} start`)
      const middle = terminal.buffer.active.viewportY
      wheel(terminal, -1)
      await framesUntil(terminal, () => terminal.buffer.active.viewportY < middle, `${label} wheel up`)
      const above = terminal.buffer.active.viewportY
      expect(above, label).toBeLessThan(middle)
      wheel(terminal, 1)
      await framesUntil(terminal, () => terminal.buffer.active.viewportY > above, `${label} wheel down`)
      expect(terminal.buffer.active.viewportY, label).toBeGreaterThan(above)
      for (const boundary of ['top', 'bottom']) {
        const line = boundary === 'top' ? 0 : terminal.buffer.active.baseY
        terminal.scrollToLine(line)
        await framesUntil(terminal, () => terminal.buffer.active.viewportY === line, `${label} ${boundary}`)
        wheel(terminal, boundary === 'top' ? -1 : 1)
        await framesUntil(terminal, () => terminal.buffer.active.viewportY === line, `${label} clamped`)
        expect(terminal.buffer.active.viewportY, `${label} ${boundary}`).toBe(line)
      }
      expect(emitted.data, label).toEqual([])
      expect(emitted.binary, label).toEqual([])
      closeTerminal(terminal)
    }
  })

  it('wheel without history stays local', async () => {
    for (const buffer of ['alternate', 'normal']) {
      const options = buffer === 'normal' ? { scrollback: 0 } : {}
      const setup = buffer === 'alternate' ? '\x1b[?1049h' : ''
      const bare = openTerminal(false, options)
      const bareEvents = collect(bare)
      await write(bare, setup)
      wheel(bare, -1)
      wheel(bare, 1)
      expect(bareEvents.data, `${buffer} fallback reachability`).toEqual(['\x1b[A', '\x1b[B'])
      expect(bareEvents.binary).toEqual([])
      closeTerminal(bare)
      const terminal = openTerminal(true, options)
      const emitted = collect(terminal)
      await write(terminal, setup)
      for (const tracking of [false, true]) {
        if (tracking) await write(terminal, GROK_STARTUP)
        wheel(terminal, -1)
        wheel(terminal, 1)
        const label = `${buffer}/tracking=${tracking}`
        expect(emitted.data, label).toEqual([])
        expect(emitted.binary, label).toEqual([])
        expect(terminal.buffer.active.baseY, label).toBe(0)
        expect(terminal.buffer.active.viewportY, label).toBe(0)
      }
      closeTerminal(terminal)
    }
  })

  it('mixed split modes retain order and input', async () => {
    const originalMatchMedia = window.matchMedia
    // setup.ts already supplies a vi.fn; spying on that same function would
    // replace the captured implementation too. Replace the property instead.
    replaceProperty(window, 'matchMedia', { value: (query: string) => ({
      ...originalMatchMedia(query), matches: query.includes('pointer: coarse'),
    }) })
    vi.mocked(getSessionBuffer).mockResolvedValueOnce({ sessionId: 'session-1', buffer: 'READY', lastSequence: 1 })
    const view = renderWithProviders(<SessionTerminal session={runningSession} />)
    await panelParsed('READY')
    await write(panel, '\x1b[H')
    const emitted = collect(panel)
    const first = write(panel, '\x1b[?1003;1006;20')
    const second = write(panel, '04;1049hALT')
    await Promise.all([first, second])
    expect(panel.buffer.active.type).toBe('alternate')
    expect(panel.buffer.active.getLine(0)!.translateToString(true)).toBe('ALT')
    vi.mocked(sendSessionInput).mockClear()
    panel.textarea!.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true, cancelable: true, key: 'ArrowUp', code: 'ArrowUp', keyCode: 38,
    }))
    panel.input('x')
    panel.paste('literal \x1b[<35;5;3M')
    view.getByRole('button', { name: 'Arrow up' }).click()
    expect(vi.mocked(sendSessionInput).mock.calls).toEqual([
      ['session-1', '\x1b[A'], ['session-1', 'x'],
      ['session-1', '\x1b[200~literal \x1b[<35;5;3M\x1b[201~'], ['session-1', '\x1b[A'],
    ])
    vi.mocked(sendSessionInput).mockClear()
    emitted.clear()
    hover(panel)
    expect(sendSessionInput).not.toHaveBeenCalled()
    expect(emitted.data).toEqual([])
    expect(emitted.binary).toEqual([])
    await write(panel, '\x1b[?1003;1006;2004;1049lNORMAL')
    expect(panel.buffer.active.type).toBe('normal')
    expect(panel.buffer.active.getLine(0)!.translateToString(true)).toContain('NORMAL')
    panel.paste('literal \x1b[<35;5;3M')
    expect(sendSessionInput).toHaveBeenCalledExactlyOnceWith('session-1', 'literal \x1b[<35;5;3M')
  })

  it('reset and instances remain independent', async () => {
    async function assertBareReports(bare: Terminal, label: string) {
      const bareEvents = collect(bare)
      await write(bare, '\x1b[?1003;1006h')
      press(bare)
      expect(bareEvents.data, `${label} bare press`).toEqual(['\x1b[<0;5;3M'])
      release()
      bareEvents.clear()
      hover(bare)
      expect(bareEvents.data, `${label} bare hover`).toEqual(['\x1b[<35;5;3M', '\x1b[<35;6;3M'])
      expect(bareEvents.binary, label).toEqual([])
      closeTerminal(bare)
    }
    const before = openTerminal(false)
    const terminal = openTerminal(true)
    await assertBareReports(before, 'created before mirror')
    const emitted = collect(terminal)
    for (const { name, mode } of protocols) {
      terminal.reset()
      await write(terminal, `\x1b[?${mode};1006hHELLO WORLD`)
      emitted.clear()
      gestures(terminal)
      expect(emitted.data, `${name} rearm`).toEqual([])
      expect(emitted.binary, `${name} rearm`).toEqual([])
      dragHello(terminal)
      expect(terminal.getSelection(), `${name} reset selection`).toBe('HELLO')
      expect(emitted.data, `${name} selection`).toEqual([])
      expect(emitted.binary, `${name} selection`).toEqual([])
    }
    closeTerminal(terminal)
    const fresh = openTerminal(true)
    const freshEvents = collect(fresh)
    await write(fresh, GROK_STARTUP + 'HELLO WORLD')
    gestures(fresh)
    dragHello(fresh)
    expect(fresh.getSelection()).toBe('HELLO')
    expect(freshEvents.data).toEqual([])
    expect(freshEvents.binary).toEqual([])
    closeTerminal(fresh)
    await assertBareReports(openTerminal(false), 'created after mirrors')
  })

  it('alt click stays local', async () => {
    function altClick(terminal: Terminal) {
      // Create the physical gesture's event pair together: synchronous layout
      // during dispatch must not change its timestamp interval on a loaded host.
      const options: MouseEventInit = {
        bubbles: true, cancelable: true, clientX: 25, clientY: 10,
        button: 0, detail: 1, altKey: true,
      }
      const down = new MouseEvent('mousedown', { ...options, buttons: 1 })
      const up = new MouseEvent('mouseup', { ...options, buttons: 0 })
      expect(up.timeStamp - down.timeStamp, 'Alt-click gesture interval').toBeLessThan(100)
      screen(terminal).dispatchEvent(down)
      document.dispatchEvent(up)
    }
    const bare = openTerminal(false, { altClickMovesCursor: true })
    const bareEvents = collect(bare)
    await write(bare, 'HELLO WORLD\x1b[1;11H')
    altClick(bare)
    expect(bareEvents.data.join(''), 'bare alt-click reachability').not.toBe('')
    expect(bareEvents.data.join('')).toMatch(/^(\x1b\[D)+$/)
    closeTerminal(bare)
    const terminal = openTerminal(true, { altClickMovesCursor: true })
    const emitted = collect(terminal)
    for (const tracking of [false, true]) {
      await write(terminal, (tracking ? GROK_STARTUP : '') + '\x1b[HHELLO WORLD\x1b[1;11H')
      altClick(terminal)
      expect(emitted.data, `tracking=${tracking}`).toEqual([])
      expect(emitted.binary, `tracking=${tracking}`).toEqual([])
    }
    terminal.input('x')
    expect(emitted.data).toEqual(['x'])
  })

  it('factory rejects incompatible mouse seam', () => {
    for (const fault of ['core', 'service', 'missing method', 'noncallable method']) {
      let allocated: Terminal | undefined
      let disposed = false
      const hook = vi.spyOn(mousePolicy, 'configureLocalTerminalMouse').mockImplementation(terminal => {
        allocated = terminal
        const internals = terminal as unknown as MouseInternals
        const core = internals._core
        const service = core.coreMouseService
        const addProtocol = service.addProtocol
        const dispose = terminal.dispose.bind(terminal)
        vi.spyOn(terminal, 'dispose').mockImplementation(() => {
          internals._core = core
          core.coreMouseService = service
          service.addProtocol = addProtocol
          disposed = true
          dispose()
        })
        if (fault === 'core') Reflect.set(internals, '_core', undefined)
        else if (fault === 'service') Reflect.set(core, 'coreMouseService', undefined)
        else Reflect.set(service, 'addProtocol', fault === 'missing method' ? undefined : 42)
        realMousePolicy(terminal)
      })
      try {
        expect(() => realFactory(layout), fault).toThrow(/mouse policy.*xterm.*addProtocol/)
      } finally {
        if (!disposed) allocated?.dispose()
        hook.mockRestore()
      }
    }
    inputControl(openTerminal(true))
  })

  it('factory disposes on setup failure', () => {
    const sentinel = new Error('second protocol registration failed')
    let allocated: Terminal | undefined
    let disposeSpy: MockInstance<Terminal['dispose']> | undefined
    const hook = vi.spyOn(mousePolicy, 'configureLocalTerminalMouse').mockImplementation(terminal => {
      allocated = terminal
      const service = (terminal as unknown as MouseInternals)._core.coreMouseService
      const original = service.addProtocol
      let registrations = 0
      service.addProtocol = function (name, protocol) {
        if (++registrations === 2) throw sentinel
        original.call(this, name, protocol)
      }
      const dispose = terminal.dispose.bind(terminal)
      disposeSpy = vi.spyOn(terminal, 'dispose').mockImplementation(() => {
        service.addProtocol = original
        dispose()
      })
      realMousePolicy(terminal)
    })
    try {
      expect(() => realFactory(layout)).toThrow(sentinel)
      expect(allocated).toBeInstanceOf(Terminal)
      expect(disposeSpy).toHaveBeenCalledTimes(1)
    } finally {
      if (disposeSpy?.mock.calls.length === 0) allocated?.dispose()
      hook.mockRestore()
    }
    inputControl(openTerminal(true))
  })
})
