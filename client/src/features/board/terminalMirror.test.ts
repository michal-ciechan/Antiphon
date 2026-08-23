import { Terminal } from '@xterm/xterm'
import { afterEach, describe, expect, it } from 'vitest'
import { CLAUDE_STARTUP } from './__fixtures__/claude-startup'
import { CODEX_STARTUP } from './__fixtures__/codex-startup'
import { GROK_STARTUP } from './__fixtures__/grok-startup'
import { createMirrorTerminal, MIRROR_TERMINAL_OPTIONS } from './terminalMirror'

const DA1_RESPONSE = '\x1b[?1;2c'
const FOCUS_OUT = '\x1b[O'
const GROK_CURSOR_SET = '\x1b]12;rgb:c8/c8/c8\x07'
const LONE_FOCUS_ENABLE = '\x1b[?1004h'
const LONE_FOCUS_DISABLE = '\x1b[?1004l'
const GROK_MOUSE_DECSET = '\x1b[?1003;1006h'

function writeComplete(terminal: Terminal, data: string): Promise<void> {
  return new Promise((resolve) => {
    terminal.write(data, () => resolve())
  })
}

function collectOnData(terminal: Terminal): string[] {
  const emitted: string[] = []
  terminal.onData((data) => { emitted.push(data) })
  return emitted
}

function cursorColorCss(terminal: Terminal): string | undefined {
  const core = terminal as unknown as {
    _core?: { _themeService?: { colors: { cursor: { css: string } } } }
  }
  return core._core?._themeService?.colors.cursor.css
}

function openOnBody(terminal: Terminal): HTMLDivElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  terminal.open(host)
  return host
}

describe('createMirrorTerminal', () => {
  const terminals: Terminal[] = []
  const hosts: HTMLDivElement[] = []

  afterEach(() => {
    for (const terminal of terminals) {
      terminal.dispose()
    }
    terminals.length = 0
    for (const host of hosts) {
      host.remove()
    }
    hosts.length = 0
  })

  function mirror(overrides?: Parameters<typeof createMirrorTerminal>[0]): Terminal {
    const terminal = createMirrorTerminal(overrides)
    terminals.push(terminal)
    return terminal
  }

  function unpatched(): Terminal {
    const terminal = new Terminal({ ...MIRROR_TERMINAL_OPTIONS, disableStdin: false })
    terminals.push(terminal)
    return terminal
  }

  it('a mirror terminal answers nothing over a real Codex startup buffer', async () => {
    const terminal = mirror({ disableStdin: false })
    const emitted = collectOnData(terminal)
    await writeComplete(terminal, CODEX_STARTUP)
    expect(emitted).toEqual([])
  })

  it('a mirror terminal answers nothing over a real Claude startup buffer', async () => {
    const terminal = mirror({ disableStdin: false })
    const emitted = collectOnData(terminal)
    await writeComplete(terminal, CLAUDE_STARTUP)
    expect(emitted).toEqual([])
  })

  it('a mirror terminal answers nothing over a real Grok startup buffer', async () => {
    const terminal = mirror({ disableStdin: false })
    const emitted = collectOnData(terminal)
    await writeComplete(terminal, GROK_STARTUP)
    expect(emitted).toEqual([])
  })

  it('an unpatched terminal does answer', async () => {
    const cases = [
      { kind: 'codex', fixture: CODEX_STARTUP },
      { kind: 'claude', fixture: CLAUDE_STARTUP },
      { kind: 'grok', fixture: GROK_STARTUP },
    ]

    for (const { kind, fixture } of cases) {
      const terminal = unpatched()
      const emitted = collectOnData(terminal)
      await writeComplete(terminal, fixture)
      const joined = emitted.join('')
      expect(joined, `${kind} fixture no longer contains a DA1 query`).toContain(DA1_RESPONSE)
      expect(joined, `${kind} fixture no longer arms focus reporting`).toContain(FOCUS_OUT)
    }
  })

  it('a colour set still reaches the renderer', async () => {
    const terminal = mirror({ disableStdin: false })
    hosts.push(openOnBody(terminal))
    const emitted = collectOnData(terminal)
    expect(cursorColorCss(terminal)).not.toBe('#c8c8c8')
    await writeComplete(terminal, GROK_CURSOR_SET)
    expect(emitted).toEqual([])
    expect(cursorColorCss(terminal)).toBe('#c8c8c8')
  })

  it('real typing is still forwarded', () => {
    const terminal = mirror({ disableStdin: false })
    const emitted = collectOnData(terminal)
    terminal.input('x')
    expect(emitted).toEqual(['x'])
  })

  it('a lone CSI ? 1004 h/l is suppressed but multi-param DECSET still reaches the built-in', async () => {
    const terminal = mirror({ disableStdin: false })
    hosts.push(openOnBody(terminal))
    const emitted = collectOnData(terminal)

    await writeComplete(terminal, LONE_FOCUS_ENABLE)
    expect(terminal.modes.sendFocusMode).toBe(false)
    terminal.focus()
    terminal.blur()
    expect(emitted).toEqual([])

    await writeComplete(terminal, LONE_FOCUS_DISABLE)
    expect(emitted).toEqual([])

    await writeComplete(terminal, GROK_MOUSE_DECSET)
    expect(emitted).toEqual([])
    expect(terminal.modes.mouseTrackingMode).toBe('any')
    expect(terminal.modes.sendFocusMode).toBe(false)
  })
})
