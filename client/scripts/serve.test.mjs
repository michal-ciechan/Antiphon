// Tests for the pure/testable parts of serve.mjs (CARD-0216 S1): mode-file parsing, the
// spawn-plan chosen per mode, and the swap decision when the mode file changes. The spawn
// function is injected throughout so nothing here launches a real Vite process.
import { EventEmitter } from 'node:events'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ServeSupervisor, parseMode, planForMode, readMode, shouldSwap } from './serve.mjs'

describe('parseMode', () => {
  it('treats "dev" (any case, trimmed) as dev', () => {
    expect(parseMode('dev')).toBe('dev')
    expect(parseMode(' Dev\n')).toBe('dev')
    expect(parseMode('DEV')).toBe('dev')
  })

  it('treats anything else, including missing/empty/garbage, as built', () => {
    expect(parseMode('built')).toBe('built')
    expect(parseMode('')).toBe('built')
    expect(parseMode(undefined)).toBe('built')
    expect(parseMode('nonsense')).toBe('built')
  })
})

describe('readMode', () => {
  let dir
  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'client-mode-'))
  })
  afterEach(() => {
    rmSync(dir, { recursive: true, force: true })
  })

  it('defaults to built when the mode file does not exist', () => {
    expect(readMode(join(dir, 'missing.mode'))).toBe('built')
  })

  it('reads dev from a file containing "dev"', () => {
    const path = join(dir, 'client.mode')
    writeFileSync(path, 'dev')
    expect(readMode(path)).toBe('dev')
  })

  it('reads built from a file containing "built"', () => {
    const path = join(dir, 'client.mode')
    writeFileSync(path, 'built')
    expect(readMode(path)).toBe('built')
  })
})

describe('planForMode', () => {
  it('dev mode: one persistent step, plain vite, no watch env', () => {
    const plan = planForMode('dev', {})
    expect(plan.mode).toBe('dev')
    expect(plan.steps).toHaveLength(1)
    expect(plan.steps[0].persistent).toBe(true)
    expect(plan.steps[0].args).toHaveLength(1) // just vite.js, no subcommand
    expect(plan.steps[0].args[0].endsWith('vite.js')).toBe(true)
  })

  it('built mode with the watcher on by default: build, preview, watch', () => {
    const plan = planForMode('built', {})
    expect(plan.mode).toBe('built')
    expect(plan.steps.map((s) => s.id)).toEqual(['build', 'preview', 'watch'])
    expect(plan.steps[0].persistent).toBe(false) // clean build runs to completion first
    expect(plan.steps[1].persistent).toBe(true) // preview stays up
    expect(plan.steps[2].persistent).toBe(true) // watcher stays up
    expect(plan.steps[2].env.ANTIPHON_VITE_KEEP_OUTDIR).toBe('1') // must not wipe dist/ on rebuild
  })

  it('ANTIPHON_CLIENT_WATCH=0 drops the watch step entirely, not just its logging', () => {
    const plan = planForMode('built', { ANTIPHON_CLIENT_WATCH: '0' })
    expect(plan.steps.map((s) => s.id)).toEqual(['build', 'preview'])
  })

  it('build and preview steps never carry ANTIPHON_VITE_KEEP_OUTDIR', () => {
    const plan = planForMode('built', {})
    expect(plan.steps[0].env.ANTIPHON_VITE_KEEP_OUTDIR).toBeUndefined()
    expect(plan.steps[1].env.ANTIPHON_VITE_KEEP_OUTDIR).toBeUndefined()
  })
})

describe('shouldSwap', () => {
  it('swaps when the desired mode differs from what is running', () => {
    expect(shouldSwap('built', 'dev')).toBe(true)
    expect(shouldSwap('dev', 'built')).toBe(true)
  })

  it('does not swap when the mode is unchanged', () => {
    expect(shouldSwap('built', 'built')).toBe(false)
    expect(shouldSwap('dev', 'dev')).toBe(false)
  })
})

// ---- ServeSupervisor: swap decision end to end, with spawn/port-check injected -----------------

function fakeChild() {
  const child = new EventEmitter()
  child.pid = Math.floor(Math.random() * 100000)
  child.stdout = new EventEmitter()
  child.kill = () => {
    child.emit('exit', null)
  }
  return child
}

describe('ServeSupervisor.pollOnce swap behaviour', () => {
  let dir
  let modeFile
  let stateFile
  let spawned

  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'serve-supervisor-'))
    modeFile = join(dir, 'client.mode')
    stateFile = join(dir, 'client.state.json')
    spawned = []
  })
  afterEach(() => {
    rmSync(dir, { recursive: true, force: true })
  })

  function makeSupervisor(env = {}) {
    return new ServeSupervisor({
      modeFile,
      stateFile,
      rebuildSentinel: join(dir, 'client.rebuild-requested'),
      env,
      log: () => {},
      waitForPortClosedFn: async () => true,
      spawnFn: (execPath, args) => {
        const child = fakeChild()
        spawned.push({ args, child })
        // The non-persistent 'build' step awaits an 'exit' event before the caller proceeds.
        if (args.includes('build') && !args.includes('--watch')) {
          queueMicrotask(() => child.emit('exit', 0))
        }
        return child
      },
    })
  }

  it('first poll with no mode file spawns the built-mode plan (default): build, preview, watch', async () => {
    const supervisor = makeSupervisor()
    await supervisor.pollOnce()
    expect(spawned).toHaveLength(3)
    expect(supervisor.currentMode).toBe('built')
    expect(supervisor.children).toHaveLength(2) // preview + watch (the completed build step isn't tracked)
  })

  it('ANTIPHON_CLIENT_WATCH=0 spawns only build + preview, no watch', async () => {
    const supervisor = makeSupervisor({ ANTIPHON_CLIENT_WATCH: '0' })
    await supervisor.pollOnce()
    expect(spawned).toHaveLength(2)
    expect(supervisor.children).toHaveLength(1) // preview only
  })

  // Real vite output, captured live from `vite build --watch`: the completion line is wrapped in
  // ANSI color codes whose closing "m" sits directly against "built" with no non-word character
  // between them - a leading `\b` in the detector regex never matches this (verified against a
  // real run; a hand-written fixture without the ANSI prefix would hide the bug).
  const ESC = String.fromCharCode(27)
  const viteBuiltLine = (ms) => `${ESC}[36mbuilt in ${ms}ms.${ESC}[39m`

  it("the watcher's own rebuild (real ANSI-wrapped 'built in Nms' on stdout) updates lastBuildAt, not just the initial build", async () => {
    const supervisor = makeSupervisor()
    await supervisor.pollOnce()
    const initialLastBuildAt = supervisor.lastBuildAt
    expect(initialLastBuildAt).not.toBeNull()

    const watchEntry = spawned.find((s) => s.args.includes('--watch'))
    expect(watchEntry).toBeDefined()

    await new Promise((resolve) => setTimeout(resolve, 5)) // ensure a distinct ISO timestamp
    watchEntry.child.stdout.emit('data', Buffer.from(`\n${viteBuiltLine(842)}\n`))

    expect(supervisor.lastBuildAt).not.toBe(initialLastBuildAt)
    const persisted = JSON.parse(readFileSync(stateFile, 'utf8'))
    expect(persisted.lastBuildAt).toBe(supervisor.lastBuildAt)
  })

  it("still detects 'built in Nms' when a pipe delivers it split across two data events", async () => {
    // Regression: a bare per-chunk regex test missed this live — the OS pipe can fragment vite's
    // output mid-word, so "built in 842ms." can arrive as "...buil" then "t in 842ms.\n...".
    const supervisor = makeSupervisor()
    await supervisor.pollOnce()
    const initialLastBuildAt = supervisor.lastBuildAt
    const watchEntry = spawned.find((s) => s.args.includes('--watch'))
    const line = viteBuiltLine(842)
    const splitAt = line.indexOf('built') + 4 // split mid-word, inside "built"

    await new Promise((resolve) => setTimeout(resolve, 5))
    watchEntry.child.stdout.emit('data', Buffer.from(`\nrendering chunks...\n${line.slice(0, splitAt)}`))
    expect(supervisor.lastBuildAt).toBe(initialLastBuildAt) // not yet - line incomplete
    watchEntry.child.stdout.emit('data', Buffer.from(`${line.slice(splitAt)}\n`))

    expect(supervisor.lastBuildAt).not.toBe(initialLastBuildAt)
  })

  it('a second poll with an unchanged mode file spawns nothing new', async () => {
    writeFileSync(modeFile, 'built')
    const supervisor = makeSupervisor()
    await supervisor.pollOnce()
    const countAfterFirst = spawned.length
    await supervisor.pollOnce()
    expect(spawned.length).toBe(countAfterFirst)
  })

  it('changing the mode file from built to dev kills the built children and spawns the dev child', async () => {
    writeFileSync(modeFile, 'built')
    const supervisor = makeSupervisor()
    await supervisor.pollOnce()
    const builtChildren = [...supervisor.children]
    expect(builtChildren.length).toBeGreaterThan(0)

    writeFileSync(modeFile, 'dev')
    await supervisor.pollOnce()

    expect(supervisor.currentMode).toBe('dev')
    // The built-mode children were killed (their fake kill() emits exit).
    expect(supervisor.children).not.toEqual(builtChildren)
    // Exactly one persistent child remains: the dev server.
    expect(supervisor.children).toHaveLength(1)
  })

  it('switching back to built re-runs a clean build before preview', async () => {
    writeFileSync(modeFile, 'dev')
    const supervisor = makeSupervisor()
    await supervisor.pollOnce()
    spawned.length = 0

    writeFileSync(modeFile, 'built')
    await supervisor.pollOnce()

    expect(spawned[0].args.some((a) => a.endsWith('vite.js'))).toBe(true)
    expect(spawned[0].args).toContain('build')
    expect(supervisor.currentMode).toBe('built')
  })
})
