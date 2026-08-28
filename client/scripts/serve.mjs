// CARD-0216 S1 — the process Aspire starts on port 17203 ("npm run serve").
//
// One long-lived process, one port, two modes:
//   built (default): a clean `vite build`, then `vite preview` serving that dist/, then
//                     `vite build --watch` (emptyOutDir off) keeping dist/ fresh in place.
//   dev:              plain `vite`, identical to `npm run dev` today.
//
// The mode lives in logs/client.mode (repo-root-relative), polled every ~1s, so switching modes
// (scripts/client-mode.ps1 -Mode dev|built) takes seconds and never needs an AppHost restart.
// State is mirrored to logs/client.state.json for client-mode.ps1 -Status to read.
//
// Dependency-free by design (only Node builtins) — this is infrastructure the client's own
// dependency tree must not be able to break.

import { spawn } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs'
import { createConnection } from 'node:net'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const CLIENT_DIR = dirname(dirname(fileURLToPath(import.meta.url)))
const REPO_ROOT = dirname(CLIENT_DIR)
const LOGS_DIR = join(REPO_ROOT, 'logs')

export const MODE_FILE = join(LOGS_DIR, 'client.mode')
export const STATE_FILE = join(LOGS_DIR, 'client.state.json')
export const REBUILD_SENTINEL = join(LOGS_DIR, 'client.rebuild-requested')

const VITE_BIN = join(CLIENT_DIR, 'node_modules', 'vite', 'bin', 'vite.js')
const POLL_MS = 1000

// ---- pure: mode file -> mode ------------------------------------------------------------------

export function parseMode(raw) {
  const trimmed = (raw ?? '').trim().toLowerCase()
  return trimmed === 'dev' ? 'dev' : 'built'
}

export function readMode(path = MODE_FILE) {
  try {
    return parseMode(readFileSync(path, 'utf8'))
  } catch {
    return 'built' // missing file, first boot on a fresh machine — built is the default (D1)
  }
}

// ---- pure: mode -> spawn plan ------------------------------------------------------------------

// Each step is { id, args, persistent, env }. `args` are passed to `node`. `persistent: false`
// means "run to completion before the next step"; `persistent: true` means "leave it running and
// track its child for kill-on-swap".
export function planForMode(mode, env = process.env) {
  if (mode === 'dev') {
    return { mode: 'dev', steps: [{ id: 'dev', args: [VITE_BIN], persistent: true, env: {} }] }
  }
  const steps = [
    { id: 'build', args: [VITE_BIN, 'build'], persistent: false, env: { NODE_ENV: 'production' } },
    { id: 'preview', args: [VITE_BIN, 'preview'], persistent: true, env: { NODE_ENV: 'production' } },
  ]
  if (env.ANTIPHON_CLIENT_WATCH !== '0') {
    steps.push({
      id: 'watch',
      args: [VITE_BIN, 'build', '--watch'],
      persistent: true,
      // Read by vite.config.ts: the watcher must rebuild in place, never wipe dist/ mid-load.
      env: { ANTIPHON_VITE_KEEP_OUTDIR: '1', NODE_ENV: 'production' },
    })
  }
  return { mode: 'built', steps }
}

// ---- pure: swap decision ------------------------------------------------------------------------

export function shouldSwap(runningMode, desiredMode) {
  return runningMode !== desiredMode
}

// ---- state file -----------------------------------------------------------------------------

export function writeState(state, path = STATE_FILE) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, JSON.stringify(state, null, 2))
}

export function readRebuildRequestStamp(path = REBUILD_SENTINEL) {
  try {
    return statSync(path).mtimeMs
  } catch {
    return null
  }
}

// ---- effectful: wait for a port to stop accepting connections ---------------------------------

function waitForPortClosed(port, { host = '127.0.0.1', timeoutMs = 10_000, intervalMs = 200 } = {}) {
  const deadline = Date.now() + timeoutMs
  return new Promise((resolve) => {
    const tryOnce = () => {
      const socket = createConnection({ port, host })
      const done = (closed) => {
        socket.removeAllListeners()
        socket.destroy()
        resolve(closed)
      }
      socket.once('connect', () => {
        if (Date.now() >= deadline) return done(false)
        socket.destroy()
        setTimeout(tryOnce, intervalMs)
      })
      socket.once('error', () => done(true))
    }
    tryOnce()
  })
}

// ---- runtime: spawns and swaps children ------------------------------------------------------

export class ServeSupervisor {
  constructor({
    spawnFn = spawn,
    execPath = process.execPath,
    env = process.env,
    log = console.log,
    modeFile = MODE_FILE,
    stateFile = STATE_FILE,
    rebuildSentinel = REBUILD_SENTINEL,
    waitForPortClosedFn = waitForPortClosed,
  } = {}) {
    this.spawnFn = spawnFn
    this.execPath = execPath
    this.env = env
    this.log = log
    this.modeFile = modeFile
    this.stateFile = stateFile
    this.rebuildSentinel = rebuildSentinel
    this.waitForPortClosedFn = waitForPortClosedFn
    this.currentMode = null
    this.children = []
    this.status = 'starting'
    this.pid = null
    this.since = null
    this.lastBuildAt = null
    this.lastRebuildStamp = readRebuildRequestStamp(this.rebuildSentinel)
  }

  snapshot() {
    return {
      mode: this.currentMode,
      pid: this.pid,
      since: this.since,
      lastBuildAt: this.lastBuildAt,
      status: this.status,
    }
  }

  persist() {
    writeState(this.snapshot(), this.stateFile)
  }

  runStep(step) {
    // The watcher (`vite build --watch`) is the one persistent step whose own completions matter
    // after the initial swap: client-mode.ps1 -Status's lastBuildAt is the delegate-footgun
    // mitigation (AGENTS.md Gotchas), so its stdout has to be scanned for vite's own
    // "built in <n>ms" line rather than left untracked like preview/dev's stdout.
    const trackBuilds = step.id === 'watch'
    const child = this.spawnFn(this.execPath, step.args, {
      cwd: CLIENT_DIR,
      env: { ...this.env, ...step.env },
      stdio: trackBuilds ? ['ignore', 'pipe', 'inherit'] : 'inherit',
    })
    if (trackBuilds && child.stdout) {
      // Line-buffer rather than regex-testing each raw chunk: a pipe delivers vite's output in
      // OS-buffer-sized fragments that can split "built in Nms" across two 'data' events, so a
      // per-chunk test misses it nondeterministically (measured live — dist/ rebuilt but
      // lastBuildAt never moved).
      let carry = ''
      child.stdout.on('data', (chunk) => {
        process.stdout.write(chunk)
        carry += chunk.toString()
        const lines = carry.split('\n')
        carry = lines.pop() ?? ''
        for (const line of lines) {
          // No leading \b: vite's ANSI color code ("\x1b[36m") ends in a word character ('m')
          // directly touching "built", so a word-boundary assertion never matches real output.
          if (/built in \d/.test(line)) {
            this.lastBuildAt = new Date().toISOString()
            this.log(`[serve] watcher rebuild landed at ${this.lastBuildAt}`)
            this.persist()
          }
        }
      })
    }
    if (!step.persistent) {
      return new Promise((resolve) => {
        child.once('exit', (code) => {
          if (code !== 0) this.log(`[serve] ${step.id} exited ${code} — continuing with what dist/ already has`)
          if (step.id === 'build') this.lastBuildAt = new Date().toISOString()
          resolve(null)
        })
        child.once('error', (err) => {
          this.log(`[serve] ${step.id} failed to start: ${err.message}`)
          resolve(null)
        })
      })
    }
    return Promise.resolve(child)
  }

  async swapTo(mode) {
    this.status = 'switching'
    this.persist()

    for (const child of this.children) {
      try {
        child.kill()
      } catch {
        // already gone
      }
    }
    this.children = []
    this.pid = null
    await this.waitForPortClosedFn(Number(this.env.VITE_PORT ?? '17203'))

    this.status = mode === 'built' ? 'building' : 'starting'
    this.persist()

    if (mode === 'built') {
      this.log(
        `[serve] built mode: inherited NODE_ENV=${this.env.NODE_ENV ?? '(unset)'}; setting child NODE_ENV=production`,
      )
    }

    const plan = planForMode(mode, this.env)
    for (const step of plan.steps) {
      const child = await this.runStep(step)
      if (child) {
        this.children.push(child)
        if (step.id === 'preview' || step.id === 'dev') this.pid = child.pid
      }
    }

    this.currentMode = mode
    this.since = new Date().toISOString()
    this.status = 'serving'
    this.persist()
  }

  async runRebuild() {
    this.status = 'building'
    this.persist()
    await this.runStep({
      id: 'build',
      args: [VITE_BIN, 'build'],
      persistent: false,
      env: { NODE_ENV: 'production' },
    })
    this.status = 'serving'
    this.persist()
  }

  async pollOnce() {
    const desired = readMode(this.modeFile)
    if (this.currentMode === null || shouldSwap(this.currentMode, desired)) {
      if (this.currentMode !== null) this.log(`[serve] mode changed ${this.currentMode} -> ${desired}, swapping`)
      await this.swapTo(desired)
      return
    }
    if (this.currentMode === 'built') {
      const stamp = readRebuildRequestStamp(this.rebuildSentinel)
      if (stamp !== null && stamp !== this.lastRebuildStamp) {
        this.lastRebuildStamp = stamp
        this.log('[serve] rebuild requested')
        await this.runRebuild()
      }
    }
  }

  shutdown() {
    for (const child of this.children) {
      try {
        child.kill()
      } catch {
        // already gone
      }
    }
  }
}

// ---- entrypoint ---------------------------------------------------------------------------------

const isMain = process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]
if (isMain) {
  if (!existsSync(VITE_BIN)) {
    console.error(`[serve] vite not found at ${VITE_BIN} — run npm install in client/ first`)
    process.exit(1)
  }
  mkdirSync(LOGS_DIR, { recursive: true })

  const supervisor = new ServeSupervisor()
  console.log(`[serve] starting — mode file: ${MODE_FILE}`)

  let polling = false
  const tick = () => {
    if (polling) return
    polling = true
    supervisor
      .pollOnce()
      .catch((err) => console.error('[serve] poll error', err))
      .finally(() => {
        polling = false
      })
  }
  tick()
  const interval = setInterval(tick, POLL_MS)

  const shutdown = () => {
    clearInterval(interval)
    supervisor.shutdown()
    process.exit(0)
  }
  process.on('SIGTERM', shutdown)
  process.on('SIGINT', shutdown)
}
