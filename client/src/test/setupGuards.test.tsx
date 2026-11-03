import { render } from '@testing-library/react'
import { createPortal } from 'react-dom'
import { describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from './utils'

// Guards for the global hooks in setup.ts that no feature test would miss (CARD-1690): the mouse
// file calls releaseDetachedStyleSheets itself, and a slower poll still passes.

let sheetsBefore: number | undefined

// The second case reads what the first left behind, after setup.ts's afterEach has run: cleanup()
// removes the rendered subtree, which jsdom 29 leaves holding its <style> sheet, and only
// releaseDetachedStyleSheets() drops it. Neither case calls the purge.
describe.sequential('setup.ts afterEach releases detached style sheets', () => {
  it('a rendered <style> adds its sheet', () => {
    sheetsBefore = document.styleSheets.length
    render(<div><style>{'.c1690-guard { color: red; }'}</style></div>)
    expect(document.styleSheets.length).toBe(sheetsBefore + 1)
  })

  it('the previous case left no sheet behind', () => {
    expect(sheetsBefore).toBeDefined()
    expect(document.styleSheets.length).toBe(sheetsBefore)
  })
})

// A failing wait must report exactly what Testing Library prints without the hook; only an
// unread poll error may skip the print (review d99c29ee D-1).
describe('setup.ts getElementError during waitFor polling', () => {
  const dumps = (message: string) => message.split('Ignored nodes: comments').length - 1
  const timedOut = (wait: Promise<unknown>) => wait.then(() => undefined, (error: Error) => error)

  it('a poll error prints the DOM only when its message is read', async () => {
    const { container } = render(<p>present</p>)
    let polled: Error | undefined
    await waitFor(() => {
      try {
        screen.getByText('absent')
      } catch (error) {
        polled = error as Error
      }
    })
    container.querySelector('p')!.textContent = 'later'
    expect(polled?.name).toBe('TestingLibraryElementError')
    expect(polled?.message).toMatch(/^Unable to find an element with the text: absent\./)
    expect(dumps(polled!.message)).toBe(1)
    expect(polled!.message).toContain('later')
    expect(polled!.message).not.toContain('present')
  })

  it('a getBy outside waitFor still prints the DOM', () => {
    render(<p>present</p>)
    let thrown: Error | undefined
    try {
      screen.getByText('absent')
    } catch (error) {
      thrown = error as Error
    }
    expect(dumps(thrown!.message)).toBe(1)
    expect(thrown!.message).toContain('present')
  })

  it('a timed-out waitFor reports the last poll error and the DOM at the timeout', async () => {
    render(<p>present</p>)
    const error = await timedOut(waitFor(() => screen.getByText('absent'), { timeout: 100 }))
    expect(error?.message).toMatch(/^Unable to find an element with the text: absent\./)
    expect(dumps(error!.message)).toBe(2)
    expect(error!.message.split('present').length - 1).toBe(2)
  })

  it('a timed-out waitFor over several matches lists each matching element', async () => {
    render(<div><p>dup</p><p>dup</p></div>)
    const error = await timedOut(waitFor(() => screen.getByText('dup'), { timeout: 100 }))
    expect(error?.message).toMatch(/^Found multiple elements with the text: dup/)
    const listing = error!.message.split('Here are the matching elements:')[1].split('(If this is intentional')[0]
    expect(listing.split('dup').length - 1).toBe(2)
  })

  it('a timed-out waitFor inside a portal container prints that container', async () => {
    // The document's outerHTML passes DEBUG_PRINT_LIMIT (7,000 characters), so its dump stops
    // well before the portal and only the poll error's own dump of the dialog can show its text.
    const rows = Array.from({ length: 1000 }, (_, i) => <p key={i}>{`row ${i}`}</p>)
    render(<div>{rows}{createPortal(<div role="dialog">dialog-only-marker</div>, document.body)}</div>)
    const dialog = screen.getByRole('dialog')
    const error = await timedOut(waitFor(() => within(dialog).getByText('absent'), { timeout: 100 }))
    expect(error?.message).toMatch(/^Unable to find an element with the text: absent\./)
    expect(error!.message).toContain('dialog-only-marker')
  })

  it('a waitFor timed out by fake timers advanced in its callback keeps the DOM', async () => {
    vi.useFakeTimers()
    try {
      render(<p>present</p>)
      const error = await timedOut(waitFor(() => {
        vi.advanceTimersByTime(100)
        screen.getByText('absent')
      }, { timeout: 50 }))
      expect(error?.message).toMatch(/^Timed out in waitFor\./)
      expect(dumps(error!.message)).toBe(1)
      expect(error!.message).toContain('present')
    } finally {
      vi.useRealTimers()
    }
  })
})
