import { Terminal, type ITerminalOptions } from '@xterm/xterm'

/**
 * Browser terminal is a *mirror* of a pty whose real terminal is OpenConsole.exe.
 * The child's queries are answered by that host; every byte this emulator would
 * generate on its own is spurious typing into the live TUI (CARD-0142). The
 * mirror never speaks.
 *
 * Query sequences are consumed (`return true`) so xterm.js never emits a report.
 * Real commands (a colour SET, CSI 1 t, multi-param DECSET) fall through
 * (`return false`) so rendering stays correct.
 */
export const MIRROR_TERMINAL_OPTIONS: ITerminalOptions = {
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
}

export function createMirrorTerminal(overrides?: ITerminalOptions): Terminal {
  const terminal = new Terminal({ ...MIRROR_TERMINAL_OPTIONS, ...overrides })
  suppressMirrorReports(terminal)
  return terminal
}

function isOscQuery(data: string): boolean {
  const trimmed = data.trim()
  return trimmed === '?' || trimmed.endsWith('?')
}

function isXtwinopsReport(params: (number | number[])[]): boolean {
  const first = params[0]
  return first === 14 || first === 16 || first === 18
}

function isLoneFocusReportMode(params: (number | number[])[]): boolean {
  return params.length === 1 && params[0] === 1004
}

function suppressMirrorReports(terminal: Terminal): void {
  const { parser } = terminal

  // DA1 (CSI c / CSI 0 c)
  parser.registerCsiHandler({ final: 'c' }, () => true)
  // DA2 (CSI > c)
  parser.registerCsiHandler({ prefix: '>', final: 'c' }, () => true)
  // DSR / CPR (CSI n, CSI ? n)
  parser.registerCsiHandler({ final: 'n' }, () => true)
  parser.registerCsiHandler({ prefix: '?', final: 'n' }, () => true)
  // DECRQM (CSI $ p, CSI ? $ p)
  parser.registerCsiHandler({ intermediates: '$', final: 'p' }, () => true)
  parser.registerCsiHandler({ prefix: '?', intermediates: '$', final: 'p' }, () => true)
  // DECRQSS (DCS $ q)
  parser.registerDcsHandler({ intermediates: '$', final: 'q' }, () => true)
  // OSC 4/10/11/12 — query only. A colour SET falls through to the renderer.
  parser.registerOscHandler(4, isOscQuery)
  parser.registerOscHandler(10, isOscQuery)
  parser.registerOscHandler(11, isOscQuery)
  parser.registerOscHandler(12, isOscQuery)
  // XTWINOPS reports only (CSI 14/16/18 t). CSI 1 t and friends still behave.
  parser.registerCsiHandler({ final: 't' }, isXtwinopsReport)
  // Focus reporting (CSI ? 1004 h/l). Lone 1004 is consumed; multi-param DECSETs
  // such as Grok's ?1003;1006h still reach the built-in (CARD-0142 W4).
  parser.registerCsiHandler({ prefix: '?', final: 'h' }, isLoneFocusReportMode)
  parser.registerCsiHandler({ prefix: '?', final: 'l' }, isLoneFocusReportMode)
}
