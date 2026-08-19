import { act, renderHook } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { useFilesViewUrlState } from './useFilesViewUrlState'

function mount(initialUrl: string) {
  return renderHook(() => ({ selection: useFilesViewUrlState(), location: useLocation() }), {
    wrapper: ({ children }) => <MemoryRouter initialEntries={[initialUrl]}>{children}</MemoryRouter>,
  })
}

describe('useFilesViewUrlState', () => {
  it('reads the open file and view straight off the query string (what a refresh restores)', () => {
    const { result } = mount('/agents/a1/files?file=docs/README.md&view=raw')
    expect(result.current.selection.selectedPath).toBe('docs/README.md')
    expect(result.current.selection.view).toBe('raw')
  })

  it('writes the file to the URL when one is selected', () => {
    const { result } = mount('/agents/a1/files')
    expect(result.current.selection.selectedPath).toBeNull()

    act(() => result.current.selection.select('src/app.ts'))
    expect(result.current.location.search).toBe('?file=src%2Fapp.ts')
    expect(result.current.selection.selectedPath).toBe('src/app.ts')
  })

  it('carries only a non-default view — the default leaves the URL clean', () => {
    const { result } = mount('/agents/a1/files?file=src/app.ts')

    act(() => result.current.selection.setView('raw'))
    expect(result.current.location.search).toBe('?file=src%2Fapp.ts&view=raw')

    // The panel passes null when the picked mode IS the file's default.
    act(() => result.current.selection.setView(null))
    expect(result.current.location.search).toBe('?file=src%2Fapp.ts')
  })

  it('drops the view when a different file is opened — each file gets its own default', () => {
    const { result } = mount('/agents/a1/files?file=src/app.ts&view=raw')

    act(() => result.current.selection.select('docs/README.md'))
    expect(result.current.location.search).toBe('?file=docs%2FREADME.md')
    expect(result.current.selection.view).toBeNull()
  })

  it('clears both params when the file is closed', () => {
    const { result } = mount('/agents/a1/files?file=src/app.ts&view=raw')

    act(() => result.current.selection.select(null))
    expect(result.current.location.search).toBe('')
  })

  it('ignores a view the app does not know', () => {
    const { result } = mount('/agents/a1/files?file=src/app.ts&view=hexdump')
    expect(result.current.selection.view).toBeNull()
  })

  it('leaves unrelated query params alone', () => {
    const { result } = mount('/agents/a1/files?tab=history')

    act(() => result.current.selection.select('src/app.ts'))
    expect(result.current.location.search).toContain('tab=history')
    expect(result.current.location.search).toContain('file=src%2Fapp.ts')
  })
})
