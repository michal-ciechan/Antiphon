import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { screen, waitFor } from './utils'

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

describe('setup.ts getElementError during waitFor polling', () => {
  const dumps = (message: string) => message.split('Ignored nodes: comments').length - 1

  it('a poll error keeps the message and name and leaves out the DOM', async () => {
    render(<p>present</p>)
    let polled: Error | undefined
    await waitFor(() => {
      try {
        screen.getByText('absent')
      } catch (error) {
        polled = error as Error
      }
    })
    expect(polled?.name).toBe('TestingLibraryElementError')
    expect(polled?.message).toMatch(/^Unable to find an element with the text: absent\./)
    expect(dumps(polled!.message)).toBe(0)
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

  it('a timed-out waitFor reports the DOM at the timeout once', async () => {
    render(<p>present</p>)
    const error = await waitFor(() => screen.getByText('absent'), { timeout: 100 }).catch((e: Error) => e)
    expect(error).toBeInstanceOf(Error)
    expect((error as Error).message).toMatch(/^Unable to find an element with the text: absent\./)
    expect(dumps((error as Error).message)).toBe(1)
    expect((error as Error).message).toContain('present')
  })
})
