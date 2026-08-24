/** 200-entry console / failed-fetch ring for the Report-bug bundle (CARD-0179 R2). */

export const CONSOLE_RING_CAP = 200

export type ConsoleRingLevel = 'error' | 'warn' | 'log' | 'unhandled' | 'fetch'

export type ConsoleRingEntry = {
  at: string
  level: ConsoleRingLevel
  message: string
  url?: string
  status?: number
  ms?: number
}

const ring: ConsoleRingEntry[] = []
let installed = false

export function pushConsoleEntry(entry: Omit<ConsoleRingEntry, 'at'> & { at?: string }): void {
  ring.push({
    at: entry.at ?? new Date().toISOString(),
    level: entry.level,
    message: entry.message,
    url: entry.url,
    status: entry.status,
    ms: entry.ms,
  })
  if (ring.length > CONSOLE_RING_CAP) ring.splice(0, ring.length - CONSOLE_RING_CAP)
}

export function getConsoleRing(): ConsoleRingEntry[] {
  return ring.slice()
}

export function resetConsoleRing(): void {
  ring.length = 0
}

function stringifyArgs(args: unknown[]): string {
  return args
    .map((arg) => {
      if (typeof arg === 'string') return arg
      if (arg instanceof Error) return arg.stack ?? arg.message
      try {
        return JSON.stringify(arg)
      } catch {
        return String(arg)
      }
    })
    .join(' ')
}

/** Patch console + window error hooks once. Safe to call repeatedly. */
export function installConsoleRing(): void {
  if (installed || typeof window === 'undefined') return
  installed = true

  const original = {
    error: console.error.bind(console),
    warn: console.warn.bind(console),
    log: console.log.bind(console),
  }

  console.error = (...args: unknown[]) => {
    pushConsoleEntry({ level: 'error', message: stringifyArgs(args) })
    original.error(...args)
  }
  console.warn = (...args: unknown[]) => {
    pushConsoleEntry({ level: 'warn', message: stringifyArgs(args) })
    original.warn(...args)
  }
  console.log = (...args: unknown[]) => {
    pushConsoleEntry({ level: 'log', message: stringifyArgs(args) })
    original.log(...args)
  }

  window.addEventListener('error', (event) => {
    pushConsoleEntry({
      level: 'unhandled',
      message: event.error instanceof Error ? (event.error.stack ?? event.error.message) : String(event.message),
    })
  })
  window.addEventListener('unhandledrejection', (event) => {
    const reason = event.reason
    pushConsoleEntry({
      level: 'unhandled',
      message: reason instanceof Error ? (reason.stack ?? reason.message) : String(reason),
    })
  })
}
