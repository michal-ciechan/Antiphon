import { mkdirSync, rmSync, rmdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { ESLint } from 'eslint'
import { describe, expect, it } from 'vitest'

// CARD-0571: `eslint .` must skip generated client/storybook-static the same way it skips dist.
// A violation placed in that directory is the witness; client source stays in the lint set.
const clientRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const generatedDir = path.join(clientRoot, 'storybook-static')
const generatedJs = path.join(generatedDir, 'card0571-bad.js')
const generatedTs = path.join(generatedDir, 'card0571-bad.ts')
const violation = 'var card0571 = 1\n'

describe('storybook-static eslint ignore', () => {
  it('skips generated storybook-static and still lints the client', async () => {
    mkdirSync(generatedDir, { recursive: true })
    writeFileSync(generatedJs, violation)
    writeFileSync(generatedTs, violation)
    try {
      const eslint = new ESLint({ cwd: clientRoot })
      expect(await eslint.isPathIgnored(generatedJs)).toBe(true)
      expect(await eslint.isPathIgnored(generatedTs)).toBe(true)
      expect(await eslint.isPathIgnored(path.join(clientRoot, 'src/main.tsx'))).toBe(false)
      expect(await eslint.isPathIgnored(path.join(clientRoot, 'dist/x.ts'))).toBe(true)

      const scanned = await eslint.lintFiles(['storybook-static/card0571-bad.ts'])
      const ruleHits = scanned.flatMap((result) => result.messages.filter((message) => message.ruleId))
      expect(ruleHits).toEqual([])

      const bare = new ESLint({ cwd: clientRoot, ignore: false })
      const forced = await bare.lintFiles(['storybook-static/card0571-bad.ts'])
      expect(forced.some((result) => result.errorCount > 0)).toBe(true)
    } finally {
      rmSync(generatedJs, { force: true })
      rmSync(generatedTs, { force: true })
      try {
        rmdirSync(generatedDir)
      } catch {
        // A real storybook build owns this directory.
      }
    }
  })
})
