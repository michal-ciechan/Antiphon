// CARD-0417 V-7 — narrow-width label-fit inspection for the reply-style picker.
//
// Renders the three real surfaces that host `ReplyStyleControl` (agent create, agent edit, project
// setup) in Storybook's isolated preview iframe at 360 and 390 CSS px, measures every one of the
// six option labels, and writes a screenshot plus a machine-checked verdict. JSDOM has no layout,
// so it cannot answer this question — see client/src/stories/card0417/ReplyStyleNarrow.stories.tsx.
//
// HTTP is stubbed inside the story; nothing is saved and no server is contacted.
// Browser: connects over CDP to the already-running Edge (BU_CDP_URL / localhost:9222).
//
// Usage:  node scripts/v7-reply-style-widths.mjs [outDir]
// Exit 0 = every label fits at both widths; exit 1 = clipping/overflow observed.

import { spawn } from 'node:child_process'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join, dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from 'playwright-core'

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..')
const outDir = resolve(process.argv[2] ?? join(clientDir, '..', '.antiphon', 'c0417-v7'))
// Default off 17209 so a Storybook already serving another checkout is never mistaken for this one.
const storybookUrl = process.env.STORYBOOK_URL ?? 'http://localhost:17219'
const storybookPort = new URL(storybookUrl).port
const cdpUrl = process.env.BU_CDP_URL ?? 'http://localhost:9222'

const WIDTHS = [360, 390]
const LABELS = ['Normal', 'Terse', 'Caveman', 'Brief', 'Phone', 'Explanatory']
const STORIES = [
  { id: 'card-0417-reply-style-at-narrow-widths--agent-create', surface: 'create' },
  { id: 'card-0417-reply-style-at-narrow-widths--agent-edit', surface: 'edit' },
  { id: 'card-0417-reply-style-at-narrow-widths--project-setup', surface: 'setup' },
]

async function isUp(url) {
  try {
    const r = await fetch(url, { signal: AbortSignal.timeout(2000) })
    return r.ok
  } catch {
    return false
  }
}

// The setup wizard hides the picker behind two Next clicks; walk to the "First agent" step.
async function reachSetupAgentStep(page) {
  const directory = page.locator('input[aria-label="Project directory"], label:has-text("Project directory") ~ * input').first()
  await directory.waitFor({ timeout: 15000 })
  await directory.fill('C:\\src\\starter')
  for (let i = 0; i < 2; i++) {
    await page.getByRole('button', { name: 'Next' }).click()
    await page.waitForTimeout(300)
  }
}

/**
 * Measures each label in the reply-style SegmentedControl. `clipped` is the real question: a label
 * whose text is wider than the box it is painted in loses characters to `text-overflow`/`hidden`.
 * `overflowsViewport` catches the other failure — a control that fits its own box but pushes the
 * document wider than the phone screen.
 */
async function measure(page, width) {
  return page.evaluate(
    ({ width, LABELS }) => {
      const controls = [...document.querySelectorAll('[class*="SegmentedControl-root"]')]
      const control = controls.find((c) =>
        LABELS.every((l) =>
          [...c.querySelectorAll('[class*="SegmentedControl-label"]')].some((el) => el.textContent.trim() === l),
        ),
      )
      if (!control) return { found: false }
      const style = getComputedStyle(control)
      const labels = LABELS.map((text) => {
        const el = [...control.querySelectorAll('[class*="SegmentedControl-label"]')].find(
          (candidate) => candidate.textContent.trim() === text,
        )
        const rect = el.getBoundingClientRect()
        const line = parseFloat(getComputedStyle(el).lineHeight) || rect.height
        return {
          text,
          clientWidth: Math.round(el.clientWidth),
          scrollWidth: Math.round(el.scrollWidth),
          // 1px of rounding slack: sub-pixel text metrics round scrollWidth up on some zoom levels.
          clipped: el.scrollWidth > el.clientWidth + 1,
          wrapped: el.getClientRects().length > 1 || rect.height > line * 1.6,
          right: Math.round(rect.right),
          overflowsViewport: rect.right > width + 1 || rect.left < -1,
        }
      })
      return {
        found: true,
        orientation: style.flexDirection === 'column' ? 'vertical' : 'horizontal',
        controlWidth: Math.round(control.getBoundingClientRect().width),
        documentScrollWidth: document.documentElement.scrollWidth,
        labels,
      }
    },
    { width, LABELS },
  )
}

async function main() {
  let storybookChild = null
  if (!(await isUp(`${storybookUrl}/index.json`))) {
    console.log('Storybook not running — booting one (this takes ~30s)…')
    storybookChild = spawn(
      process.platform === 'win32' ? 'npx.cmd' : 'npx',
      ['storybook', 'dev', '-p', storybookPort, '--no-open', '--ci'],
      { cwd: clientDir, stdio: 'ignore', shell: process.platform === 'win32' },
    )
    const deadline = Date.now() + 180_000
    while (Date.now() < deadline && !(await isUp(`${storybookUrl}/index.json`)))
      await new Promise((r) => setTimeout(r, 2000))
    if (!(await isUp(`${storybookUrl}/index.json`))) throw new Error('Storybook did not come up')
  }

  const browser = await chromium.connectOverCDP(cdpUrl)
  const context = browser.contexts()[0] ?? (await browser.newContext())
  const page = await context.newPage()
  page.on('console', (m) => {
    if (m.text().startsWith('[v7]')) console.log(`    ${m.text()}`)
  })

  mkdirSync(outDir, { recursive: true })
  const results = []
  for (const width of WIDTHS) {
    for (const story of STORIES) {
      await page.setViewportSize({ width, height: 900 })
      await page.goto(`${storybookUrl}/iframe.html?viewMode=story&id=${story.id}`, { waitUntil: 'networkidle' })
      await page.addStyleTag({
        content: '*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}',
      })
      if (story.surface === 'setup') await reachSetupAgentStep(page)
      await page.locator('[class*="SegmentedControl-label"]', { hasText: 'Explanatory' }).first().waitFor({ timeout: 20000 })
      await page.waitForTimeout(400)
      // These modals scroll their own body, so the picker sits below the fold and `fullPage` would
      // photograph the top of the modal instead of the control under inspection.
      await page.locator('[class*="SegmentedControl-label"]', { hasText: 'Explanatory' }).first()
        .evaluate((el) => el.scrollIntoView({ block: 'center' }))
      await page.waitForTimeout(300)
      const m = await measure(page, width)
      const file = join(outDir, `${story.surface}-${width}.png`)
      await page.screenshot({ path: file, timeout: 20000 })
      results.push({ surface: story.surface, width, file, selected: 'Normal', ...m })

      // Again with Phone chosen: its description is the longest of the six, so this is where a
      // narrow column would clip or push the modal wider if anything were going to.
      await page.locator('[class*="SegmentedControl-label"]', { hasText: 'Phone' }).first().click()
      await page.waitForTimeout(300)
      const phoneMeasure = await measure(page, width)
      const phoneFile = join(outDir, `${story.surface}-${width}-phone.png`)
      await page.screenshot({ path: phoneFile, timeout: 20000 })
      results.push({ surface: story.surface, width, file: phoneFile, selected: 'Phone', ...phoneMeasure })

      const bad = [...(m.labels ?? []), ...(phoneMeasure.labels ?? [])].filter((l) => l.clipped || l.overflowsViewport)
      console.log(
        `  ${m.found ? (bad.length === 0 ? '✓' : '✗') : '?'} ${story.surface} @ ${width}px — ` +
          `${m.orientation ?? 'n/a'}, ${bad.length === 0 ? 'all six labels fit' : `clipped/overflowing: ${bad.map((l) => l.text).join(', ')}`}`,
      )
    }
  }
  await page.close()

  const failures = results.filter(
    (r) => !r.found || r.labels.some((l) => l.clipped || l.overflowsViewport),
  )
  const verdict = {
    card: 'CARD-0417',
    check: 'V-7 narrow-width label fit',
    widths: WIDTHS,
    surfaces: STORIES.map((s) => s.surface),
    capturedAt: new Date().toISOString(),
    verdict: failures.length === 0 ? 'PASS — no clipping and no horizontal overflow' : 'FAIL',
    results,
  }
  writeFileSync(join(outDir, 'verdict.json'), JSON.stringify(verdict, null, 2))
  console.log(`\n${verdict.verdict}\nWrote ${results.length} screenshots + verdict.json to ${outDir}`)

  if (storybookChild) storybookChild.kill()
  process.exit(failures.length === 0 ? 0 : 1)
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
