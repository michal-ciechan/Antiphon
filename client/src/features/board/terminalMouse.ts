import type { Terminal } from '@xterm/xterm'

interface MouseProtocolRegistry {
  addProtocol(name: string, protocol: { events: number; restrict: () => boolean }): void
}

/** Keep pointer interaction local, including selection and normal-buffer scrolling. */
export function configureLocalTerminalMouse(terminal: Terminal): void {
  // xterm 6.0.0's private, per-instance registry is the smallest seam that retains
  // normal DECSET parsing (including mixed modes) while restoring local selection
  // and viewport wheel ownership. Recheck its protocol inventory on xterm upgrades.
  // Register fresh entries: the original protocol objects are shared by terminals.
  const service = (terminal as unknown as {
    _core?: { coreMouseService?: MouseProtocolRegistry }
  })._core?.coreMouseService
  if (typeof service?.addProtocol !== 'function') {
    throw new Error('Terminal mouse policy requires xterm coreMouseService.addProtocol')
  }
  for (const name of ['X10', 'VT200', 'DRAG', 'ANY']) {
    service.addProtocol(name, { events: 0, restrict: () => false })
  }

  // This vetoes core mouse/arrow generation, leaving the separate viewport wheel
  // listener intact. In particular, alternate buffers must not synthesize arrows.
  terminal.attachCustomWheelEventHandler(() => false)
  terminal.options.altClickMovesCursor = false
}
