import { describe, expect, it } from 'vitest'
import { ESLint } from 'eslint'

/**
 * The lint gate (CARD-0076): `npm run lint` was red on master for long enough to accumulate 31
 * problems, because nothing gated it — every agent stepped around the standing red per-file and
 * the check meant nothing. This repo has no CI, so the enforceable surface is the test suite
 * every client-touching agent already runs. This test IS the gate: it fails `npm test` on ANY
 * lint problem, warnings included (matching `npm run lint`'s --max-warnings 0, so the two checks
 * cannot disagree — a check must mean one thing regardless of who runs it).
 *
 * If this goes red on your change: fix the finding, or suppress it with an individually justified,
 * dated eslint-disable comment. Do NOT relax the rule in eslint.config.js — that is the lint
 * equivalent of widening a test timeout.
 */
describe('lint gate', () => {
  it('eslint reports zero problems across the client', { timeout: 180_000 }, async () => {
    const eslint = new ESLint({ cwd: process.cwd() })
    const results = await eslint.lintFiles(['.'])
    const problems = results.filter((r) => r.errorCount > 0 || r.warningCount > 0)
    const formatter = await eslint.loadFormatter('stylish')
    expect(problems, await formatter.format(problems)).toEqual([])
  })
})
