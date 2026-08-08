import { act, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import type { FilesViewSelection } from './FilesReviewPanel'
import { useFilesViewUrlState } from './useFilesViewUrlState'

let selection: FilesViewSelection

function Probe() {
  selection = useFilesViewUrlState()
  const location = useLocation()
  return (
    <>
      <span data-testid="search">{location.search}</span>
      <span data-testid="path">{String(selection.selectedPath)}</span>
      <span data-testid="view">{String(selection.view)}</span>
    </>
  )
}

function mount(initialUrl: string) {
  return render(
    <MemoryRouter initialEntries={[initialUrl]}>
      <Probe />
    </MemoryRouter>,
  )
}

const search = () => screen.getByTestId('search').textContent
const path = () => screen.getByTestId('path').textContent
const view = () => screen.getByTestId('view').textContent

describe('useFilesViewUrlState', () => {
  it('reads the open file and view straight off the query string (what a refresh restores)', () => {
    mount('/agents/a1/files?file=docs/README.md&view=raw')
    expect(path()).toBe('docs/README.md')
    expect(view()).toBe('raw')
  })

  it('writes the file to the URL when one is selected', () => {
    mount('/agents/a1/files')
    expect(path()).toBe('null')

    act(() => selection.select('src/app.ts'))
    expect(search()).toBe('?file=src%2Fapp.ts')
    expect(path()).toBe('src/app.ts')
  })

  it('carries only a non-default view — the default leaves the URL clean', () => {
    mount('/agents/a1/files?file=src/app.ts')

    act(() => selection.setView('raw'))
    expect(search()).toBe('?file=src%2Fapp.ts&view=raw')

    // The panel passes null when the picked mode IS the file's default.
    act(() => selection.setView(null))
    expect(search()).toBe('?file=src%2Fapp.ts')
  })

  it('drops the view when a different file is opened — each file gets its own default', () => {
    mount('/agents/a1/files?file=src/app.ts&view=raw')

    act(() => selection.select('docs/README.md'))
    expect(search()).toBe('?file=docs%2FREADME.md')
    expect(view()).toBe('null')
  })

  it('clears both params when the file is closed', () => {
    mount('/agents/a1/files?file=src/app.ts&view=raw')

    act(() => selection.select(null))
    expect(search()).toBe('')
  })

  it('ignores a view the app does not know', () => {
    mount('/agents/a1/files?file=src/app.ts&view=hexdump')
    expect(view()).toBe('null')
  })

  it('leaves unrelated query params alone', () => {
    mount('/agents/a1/files?tab=history')

    act(() => selection.select('src/app.ts'))
    expect(search()).toContain('tab=history')
    expect(search()).toContain('file=src%2Fapp.ts')
  })
})
