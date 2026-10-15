import { spawn } from 'node:child_process'
import { once } from 'node:events'
import { createServer } from 'node:net'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

const receiptScript = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../scripts/host-stats-hub-receipt.mjs')

it('bounds the receipt timeout while the SignalR connection never replies', async () => {
  const sockets = new Set<import('node:net').Socket>()
  // A Windows child that exits with the socket open aborts it, so the peer reset is expected here.
  const socketErrors: NodeJS.ErrnoException[] = []
  // The script arms its timeout before connection.start(), so the hub's first accepted socket comes
  // after the timer and the limit can start there. Starting it at spawn charged Node start-up and
  // the SignalR require to the test's 2300 ms limit (timeout plus 2 s), which a loaded host
  // stretched past the limit with the script still correct (CARD-1246). The script arms its own
  // 0.3 s timer after the require.
  let connected!: () => void
  const firstConnection = new Promise<void>(resolve => { connected = resolve })
  const server = createServer(socket => {
    connected()
    sockets.add(socket)
    socket.on('error', error => socketErrors.push(error))
    socket.on('close', () => sockets.delete(socket))
  })
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')

  let child: ReturnType<typeof spawn> | undefined
  try {
    const address = server.address()
    if (!address || typeof address === 'string') throw new Error('expected a TCP address')
    const spawned = spawn(process.execPath, [
      // The override lets mutation verification run the pre-fix script from a scratch copy.
      process.env['ANTIPHON_HOST_STATS_RECEIPT_TEST_SCRIPT'] ?? receiptScript,
      '--api', `http://127.0.0.1:${address.port}`,
      '--count', '1',
      '--timeout-seconds', '0.3',
    ], { stdio: ['ignore', 'pipe', 'pipe'] })
    child = spawned
    let stderr = ''
    spawned.stderr.setEncoding('utf8')
    spawned.stderr.on('data', chunk => { stderr += chunk })

    const result = await new Promise<{ code: number | null, signal: NodeJS.Signals | null }>((resolve, reject) => {
      let limit: ReturnType<typeof setTimeout> | undefined
      void firstConnection.then(() => {
        limit = setTimeout(() => reject(new Error('receipt exceeded timeout plus 2 seconds')), 2300)
      })
      spawned.once('error', error => {
        clearTimeout(limit)
        reject(error)
      })
      spawned.once('close', (code, signal) => {
        clearTimeout(limit)
        resolve({ code, signal })
      })
    })
    expect(result.signal).toBeNull()
    expect(result.code).toBe(1)
    expect(stderr).toMatch(/Host stats receipt failed: timed out after 0 receipts/)
  } finally {
    child?.kill()
    for (const socket of sockets) socket.destroy()
    await new Promise<void>(resolve => server.close(() => resolve()))
  }
  expect(socketErrors.filter(error => error.code !== 'ECONNRESET')).toEqual([])
})
