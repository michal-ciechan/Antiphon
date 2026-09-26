import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { once } from 'node:events'
import { createServer } from 'node:net'
import { fileURLToPath } from 'node:url'
import { test } from 'node:test'

const receiptScript = fileURLToPath(new URL('./host-stats-hub-receipt.mjs', import.meta.url))

test('receipt timeout includes a SignalR connection that never replies', async () => {
  const sockets = new Set()
  const server = createServer(socket => {
    sockets.add(socket)
    socket.on('close', () => sockets.delete(socket))
  })
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')

  const child = spawn(process.execPath, [
    receiptScript,
    '--api', `http://127.0.0.1:${server.address().port}`,
    '--count', '1',
    '--timeout-seconds', '0.3',
  ], { stdio: ['ignore', 'pipe', 'pipe'] })
  let stderr = ''
  child.stderr.setEncoding('utf8')
  child.stderr.on('data', chunk => { stderr += chunk })

  try {
    const result = await new Promise((resolve, reject) => {
      const limit = setTimeout(() => reject(new Error('receipt exceeded timeout plus 2 seconds')), 2300)
      child.once('error', error => {
        clearTimeout(limit)
        reject(error)
      })
      child.once('close', (code, signal) => {
        clearTimeout(limit)
        resolve({ code, signal })
      })
    })
    assert.equal(result.signal, null)
    assert.equal(result.code, 1)
    assert.match(stderr, /Host stats receipt failed: timed out after 0 receipts/)
  } finally {
    child.kill()
    for (const socket of sockets) socket.destroy()
    await new Promise(resolve => server.close(resolve))
  }
})
