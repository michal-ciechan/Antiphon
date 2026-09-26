import { EventEmitter } from 'node:events'
import { describe, expect, it } from 'vitest'
import { createWsProxyTrace } from './proxy-trace.mjs'

function harness() {
  const lines = []
  const proxy = new EventEmitter()
  const configure = createWsProxyTrace({
    label: '/api',
    log: (line) => lines.push(line),
    appendLine: () => {},
    now: () => new Date('2026-09-25T15:53:48.000Z'),
  })
  configure(proxy)
  return { lines, proxy }
}

function socket(port) {
  const client = new EventEmitter()
  client.remoteAddress = '127.0.0.1'
  client.remotePort = port
  return client
}

describe('phone-home proxy trace', () => {
  it('names the runner and which socket closed first', () => {
    const { lines, proxy } = harness()
    const client = socket(4242)
    const target = new EventEmitter()
    proxy.emit(
      'proxyReqWs',
      { socket: target },
      { url: '/api/session-runners/server2/connect', socket: client },
      client,
    )
    expect(lines.some((line) => line.includes('runner=server2') && line.includes('4242'))).toBe(true)
    client.emit('close', false)
    target.emit('close', true)
    expect(lines.some((line) => line.includes('client socket closed') && line.includes('hadError=false'))).toBe(true)
    expect(lines.some((line) => line.includes('target socket closed') && line.includes('hadError=true'))).toBe(true)
  })

  it('stays quiet for a browser hub', () => {
    const { lines, proxy } = harness()
    const client = socket(9)
    proxy.emit('proxyReqWs', { socket: new EventEmitter() }, { url: '/hubs/antiphon', socket: client }, client)
    expect(lines).toEqual([])
  })

  it('names a reset on the connect upgrade', () => {
    const { lines, proxy } = harness()
    const client = socket(4242)
    proxy.emit('error', { code: 'ECONNRESET' }, { url: '/api/session-runners/server2/connect', socket: client })
    expect(lines.some((line) => line.includes('ws error ECONNRESET'))).toBe(true)
  })
})
