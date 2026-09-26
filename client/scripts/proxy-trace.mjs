// CARD-0716 D-6. Logging-only trace for the Vite /api websocket proxy.
// Node builtins only: the client's dependency tree must not be able to break serve.

import { appendFileSync, mkdirSync, statSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const LOG_PATH = join(dirname(dirname(fileURLToPath(import.meta.url))), '..', 'logs', 'client-proxy.log')
const MAX_BYTES = 1024 * 1024

function defaultAppend(line) {
  mkdirSync(dirname(LOG_PATH), { recursive: true })
  try {
    if (statSync(LOG_PATH).size > MAX_BYTES)
      writeFileSync(LOG_PATH, '')
  } catch {
    // the file is created by the append below
  }
  appendFileSync(LOG_PATH, line.endsWith('\n') ? line : `${line}\n`)
}

function pathOf(url) {
  return typeof url === 'string' ? url.split('?')[0] : ''
}

function isConnect(url) {
  const path = pathOf(url)
  return path.includes('/session-runners/') && path.endsWith('/connect')
}

function runnerId(url) {
  const match = pathOf(url).match(/\/session-runners\/([^/]+)\/connect$/)
  return match ? match[1] : 'unknown'
}

export function createWsProxyTrace(options = {}) {
  const label = options.label ?? '/api'
  const log = options.log ?? ((line) => { console.log(line) })
  const appendLine = options.appendLine ?? defaultAppend
  const now = options.now ?? (() => new Date())

  const write = (text) => {
    const line = `${now().toISOString()} [proxy] ${text}`
    log(line)
    appendLine(line)
  }

  return function configure(proxy) {
    proxy.on('proxyReqWs', (proxyReq, req, socket) => {
      if (!isConnect(req?.url))
        return
      const remote = socket?.remoteAddress != null && socket?.remotePort != null
        ? `${socket.remoteAddress}:${socket.remotePort}`
        : String(socket?.remotePort ?? '')
      write(`${label} ws proxyReqWs runner=${runnerId(req.url)} remote=${remote}`)
      socket?.on?.('close', (hadError) => {
        write(`ws client socket closed hadError=${Boolean(hadError)}`)
      })
      const attachTarget = (target) => {
        target?.on?.('close', (hadError) => {
          write(`ws target socket closed hadError=${Boolean(hadError)}`)
        })
      }
      if (proxyReq?.socket)
        attachTarget(proxyReq.socket)
      else
        proxyReq?.on?.('socket', attachTarget)
    })

    proxy.on('error', (err, req) => {
      if (!isConnect(req?.url))
        return
      write(`ws error ${err?.code ?? 'unknown'}`)
    })

    proxy.on('close', (req) => {
      if (!isConnect(req?.url))
        return
      write(`ws close runner=${runnerId(req?.url)}`)
    })
  }
}
