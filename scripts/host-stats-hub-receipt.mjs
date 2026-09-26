import { createRequire } from 'node:module'

const require = createRequire(new URL('../client/package.json', import.meta.url))
const { HubConnectionBuilder, LogLevel } = require('@microsoft/signalr')

function option(name, fallback) {
  const index = process.argv.indexOf(name)
  return index < 0 ? fallback : process.argv[index + 1]
}

const api = option('--api')
const count = Number(option('--count', '2'))
const timeoutSeconds = Number(option('--timeout-seconds', '20'))
if (!api || !Number.isInteger(count) || count < 1 || !Number.isFinite(timeoutSeconds) || timeoutSeconds <= 0) {
  console.error('Usage: node scripts/host-stats-hub-receipt.mjs --api <url> --count <positive integer> --timeout-seconds <positive number>')
  process.exit(2)
}

const connection = new HubConnectionBuilder()
  .withUrl(new URL('/hubs/antiphon', api).toString())
  .configureLogging(LogLevel.Error)
  .build()

let received = 0
let last = []
connection.on('HostStatsUpdated', payload => {
  if (received < count) {
    received++
    last = payload
  }
})

const deadline = Date.now() + timeoutSeconds * 1000
const timeout = setTimeout(() => {
  console.error(`Host stats receipt failed: timed out after ${received} receipts`)
  process.exit(1)
}, timeoutSeconds * 1000)
timeout.unref()

try {
  await connection.start()
  await connection.invoke('JoinGroup', 'hosts')
  while (received < count && Date.now() < deadline) {
    await new Promise(resolve => setTimeout(resolve, 100))
  }
  if (received < count) throw new Error(`timed out after ${received} receipts`)
  console.log(`RECEIPT ${received}`)
  for (const host of last) console.log(`HOST ${host.hostId} ${host.state} ${host.observedAt}`)
} catch (error) {
  console.error(`Host stats receipt failed: ${error.message}`)
  process.exitCode = 1
} finally {
  try {
    await connection.stop()
  } finally {
    clearTimeout(timeout)
  }
}
