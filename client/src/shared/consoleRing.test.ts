import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import {
  CONSOLE_RING_CAP,
  getConsoleRing,
  installConsoleRing,
  pushConsoleEntry,
  resetConsoleRing,
} from './consoleRing'

describe('consoleRing', () => {
  beforeEach(() => {
    resetConsoleRing()
    installConsoleRing()
  })

  afterEach(() => {
    resetConsoleRing()
    vi.unstubAllGlobals()
  })

  it('caps at 200 entries', () => {
    for (let i = 0; i < CONSOLE_RING_CAP + 25; i++) {
      pushConsoleEntry({ level: 'log', message: `line-${i}` })
    }
    const entries = getConsoleRing()
    expect(entries).toHaveLength(CONSOLE_RING_CAP)
    expect(entries[0].message).toBe('line-25')
    expect(entries[CONSOLE_RING_CAP - 1].message).toBe(`line-${CONSOLE_RING_CAP + 24}`)
  })

  it('records console.error / warn / log', () => {
    console.error('boom')
    console.warn('careful')
    console.log('ok')
    const levels = getConsoleRing().map((e) => e.level)
    expect(levels).toEqual(['error', 'warn', 'log'])
    expect(getConsoleRing().map((e) => e.message)).toEqual(['boom', 'careful', 'ok'])
  })

  it('records failed fetch entries from apiClient', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('nope', { status: 503, statusText: 'Unavailable' })),
    )
    await expect(apiGet('/agents')).rejects.toThrow()
    const entry = getConsoleRing().find((e) => e.level === 'fetch')
    expect(entry).toMatchObject({
      level: 'fetch',
      url: '/agents',
      status: 503,
    })
    expect(entry?.message).toContain('GET /agents 503')
  })
})
