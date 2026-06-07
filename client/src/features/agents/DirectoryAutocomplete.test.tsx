import { HttpResponse, http } from 'msw'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { DirectoryBrowseResponse } from '../../api/filesystem'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { DirectoryAutocomplete } from './DirectoryAutocomplete'

/**
 * Registers an MSW handler for the browse endpoint that branches on the `path`
 * query param. The `respond` callback receives the requested path and returns the
 * body. A permissive default keeps the focus/empty request from tripping
 * `onUnhandledRequest: 'error'`.
 */
function useBrowseHandler(respond: (path: string) => DirectoryBrowseResponse) {
  server.use(
    http.get('/api/filesystem/browse', ({ request }) => {
      const path = new URL(request.url).searchParams.get('path') ?? ''
      return HttpResponse.json(respond(path))
    }),
  )
}

const emptyDrives: DirectoryBrowseResponse = {
  normalizedPath: '',
  exists: false,
  isDrivesListing: true,
  suggestions: [],
}

/** Stateful wrapper so typing and toggling actually update controlled props. */
function Harness({
  onCreateIfMissingChange,
  initialValue = '',
}: {
  onCreateIfMissingChange?: (v: boolean) => void
  initialValue?: string
}) {
  const [value, setValue] = useState(initialValue)
  const [createIfMissing, setCreateIfMissing] = useState(false)

  return (
    <DirectoryAutocomplete
      value={value}
      onChange={setValue}
      createIfMissing={createIfMissing}
      onCreateIfMissingChange={(v) => {
        onCreateIfMissingChange?.(v)
        setCreateIfMissing(v)
      }}
    />
  )
}

describe('DirectoryAutocomplete', () => {
  it('shows matching suggestions after typing a prefix', async () => {
    useBrowseHandler((path) => {
      if (path.startsWith('C:/sr')) {
        return {
          normalizedPath: 'C:/sr',
          exists: false,
          isDrivesListing: false,
          suggestions: ['C:/src', 'C:/srv'],
        }
      }
      return emptyDrives
    })

    renderWithProviders(<Harness />)

    const input = screen.getByRole('textbox', { name: 'Working directory' })
    await userEvent.click(input)
    await userEvent.type(input, 'C:/sr')

    // Mantine renders options in a dropdown once data arrives; debounce is 200ms. Options now
    // render highlighted (split) text, so match on the option's accessible name, not a text node.
    // The dropdown lives in a ScrollArea that RTL treats as hidden, hence `hidden: true`.
    expect(await screen.findByRole('option', { hidden: true, name: 'C:/src' })).toBeInTheDocument()
    expect(screen.getByRole('option', { hidden: true, name: 'C:/srv' })).toBeInTheDocument()
  })

  it('surfaces and highlights a fuzzy (non-prefix) match', async () => {
    useBrowseHandler((path) => {
      if (path.startsWith('C:/src/lea')) {
        return {
          normalizedPath: 'C:/src/lea',
          exists: false,
          isDrivesListing: false,
          suggestions: ['C:/src/torquay-leander'],
        }
      }
      return emptyDrives
    })

    renderWithProviders(<Harness />)

    const input = screen.getByRole('textbox', { name: 'Working directory' })
    await userEvent.click(input)
    await userEvent.type(input, 'C:/src/lea')

    // The option appears even though the name doesn't start with "lea"...
    expect(
      await screen.findByRole('option', { hidden: true, name: 'C:/src/torquay-leander' }),
    ).toBeInTheDocument()
    // ...and the matched "lea" is highlighted in a <mark>.
    const mark = screen.getByText('lea')
    expect(mark.tagName).toBe('MARK')
  })

  it('shows the warning and create toggle for a missing path', async () => {
    useBrowseHandler((path) => {
      if (path.startsWith('C:/nope')) {
        return {
          normalizedPath: 'C:/nope',
          exists: false,
          isDrivesListing: false,
          suggestions: [],
        }
      }
      return emptyDrives
    })

    renderWithProviders(<Harness />)

    const input = screen.getByRole('textbox', { name: 'Working directory' })
    await userEvent.type(input, 'C:/nope')

    expect(await screen.findByText('Directory does not exist')).toBeInTheDocument()
    expect(screen.getByLabelText('Create this directory')).toBeInTheDocument()
  })

  it('calls onCreateIfMissingChange(true) when the toggle is clicked', async () => {
    const onCreateIfMissingChange = vi.fn()
    useBrowseHandler((path) => {
      if (path.startsWith('C:/nope')) {
        return {
          normalizedPath: 'C:/nope',
          exists: false,
          isDrivesListing: false,
          suggestions: [],
        }
      }
      return emptyDrives
    })

    renderWithProviders(<Harness onCreateIfMissingChange={onCreateIfMissingChange} />)

    const input = screen.getByRole('textbox', { name: 'Working directory' })
    await userEvent.type(input, 'C:/nope')

    const toggle = await screen.findByLabelText('Create this directory')
    await userEvent.click(toggle)

    expect(onCreateIfMissingChange).toHaveBeenCalledWith(true)
    await waitFor(() => expect(toggle).toBeChecked())
  })

  it('shows no warning for an existing path', async () => {
    useBrowseHandler((path) => {
      if (path.startsWith('C:/src')) {
        return {
          normalizedPath: 'C:/src',
          exists: true,
          isDrivesListing: false,
          suggestions: ['C:/src'],
        }
      }
      return emptyDrives
    })

    renderWithProviders(<Harness />)

    const input = screen.getByRole('textbox', { name: 'Working directory' })
    await userEvent.type(input, 'C:/src')

    // Wait for the query to settle for the typed path before asserting absence.
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'Working directory' })).toHaveValue('C:/src'),
    )
    await waitFor(() =>
      expect(screen.getByRole('option', { hidden: true, name: 'C:/src' })).toBeInTheDocument(),
    )

    expect(screen.queryByText('Directory does not exist')).not.toBeInTheDocument()
  })
})
