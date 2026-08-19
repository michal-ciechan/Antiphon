import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { server } from '../../test/mocks/server'
import { renderWithProviders } from '../../test/utils'
import { IgnorePathModal } from './IgnorePathModal'
import { ignorePatternFor } from './ignorePattern'

const AGENT = 'a1'

let previewRequests: string[] = []
let addRequests: string[] = []

afterEach(() => {
  previewRequests = []
  addRequests = []
})

/** Stands in for git: a bare name matches at any depth, a leading slash pins it to the root. */
function fakeGitMatch(pattern: string, paths: string[]): string[] {
  const clean = pattern.trim().replace(/\/$/, '')
  if (clean.startsWith('/')) {
    const anchored = clean.slice(1)
    return paths.filter((p) => p === anchored || p.startsWith(`${anchored}/`))
  }
  return paths.filter((p) => p === clean || p.split('/').includes(clean))
}

const WORKSPACE = [
  'bin-check/a.dll',
  'nested/bin-check/b.dll',
  'server/Program.cs',
]

function mockApi(options: { tracked?: string[] } = {}) {
  server.use(
    http.post(`*/api/agents/${AGENT}/files/ignore/preview`, async ({ request }) => {
      const body = (await request.json()) as { pattern: string }
      previewRequests.push(body.pattern)
      return HttpResponse.json({
        pattern: body.pattern,
        matches: fakeGitMatch(body.pattern, WORKSPACE),
        truncated: false,
        trackedMatches: options.tracked ?? [],
      })
    }),
    http.post(`*/api/agents/${AGENT}/files/ignore`, async ({ request }) => {
      const body = (await request.json()) as { pattern: string }
      addRequests.push(body.pattern)
      return HttpResponse.json({
        pattern: body.pattern,
        gitIgnorePath: 'C:/repo/.gitignore',
        removed: fakeGitMatch(body.pattern, WORKSPACE).length,
      })
    }),
  )
}

describe('ignorePatternFor', () => {
  it('a folder gets a trailing slash so it cannot match a same-named file', () => {
    expect(ignorePatternFor('server/bin-check', true, 'name')).toBe('bin-check/')
    expect(ignorePatternFor('server/bin-check', true, 'path')).toBe('/server/bin-check/')
  })

  it('a file is matched by bare name, or by anchored path', () => {
    expect(ignorePatternFor('docs/notes.md', false, 'name')).toBe('notes.md')
    expect(ignorePatternFor('docs/notes.md', false, 'path')).toBe('/docs/notes.md')
  })

  it('a root-level target still anchors under the path scope', () => {
    expect(ignorePatternFor('bin-check', true, 'name')).toBe('bin-check/')
    expect(ignorePatternFor('bin-check', true, 'path')).toBe('/bin-check/')
  })

  it('tolerates stray slashes on the incoming path', () => {
    expect(ignorePatternFor('/server/bin-check/', true, 'path')).toBe('/server/bin-check/')
  })
})

describe('IgnorePathModal', () => {
  it('defaults to matching the name anywhere and lists every match', async () => {
    mockApi()
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'nested/bin-check', isFolder: true }}
        onClose={vi.fn()}
      />,
    )

    const input = await screen.findByTestId('ignore-pattern-input')
    expect(input).toHaveValue('bin-check/')

    const list = await screen.findByTestId('ignore-match-list')
    await waitFor(() => {
      expect(within(list).getByText('bin-check/a.dll')).toBeInTheDocument()
    })
    expect(within(list).getByText('nested/bin-check/b.dll')).toBeInTheDocument()
    expect(within(list).queryByText('server/Program.cs')).not.toBeInTheDocument()
  })

  it('switching to "only this one" anchors the line and narrows the list', async () => {
    mockApi()
    const user = userEvent.setup()
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'bin-check', isFolder: true }}
        onClose={vi.fn()}
      />,
    )
    await screen.findByTestId('ignore-match-list')

    await user.click(screen.getByRole('radio', { name: /only this one/i }))

    await waitFor(() => {
      expect(screen.getByTestId('ignore-pattern-input')).toHaveValue('/bin-check/')
    })
    await waitFor(() => {
      const list = screen.getByTestId('ignore-match-list')
      expect(within(list).queryByText('nested/bin-check/b.dll')).not.toBeInTheDocument()
    })
    expect(within(screen.getByTestId('ignore-match-list')).getByText('bin-check/a.dll')).toBeInTheDocument()
  })

  it('a hand-edited line is previewed as typed', async () => {
    mockApi()
    const user = userEvent.setup()
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'bin-check', isFolder: true }}
        onClose={vi.fn()}
      />,
    )
    const input = await screen.findByTestId('ignore-pattern-input')

    await user.clear(input)
    await user.type(input, 'server/Program.cs')

    await waitFor(() => {
      const list = screen.getByTestId('ignore-match-list')
      expect(within(list).getByText('server/Program.cs')).toBeInTheDocument()
    })
    // The preview follows the text, not the radio.
    expect(previewRequests).toContain('server/Program.cs')
  })

  it('says so plainly when a line matches nothing', async () => {
    mockApi()
    const user = userEvent.setup()
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'bin-check', isFolder: true }}
        onClose={vi.fn()}
      />,
    )
    const input = await screen.findByTestId('ignore-pattern-input')

    await user.clear(input)
    await user.type(input, 'nothing-matches-this')

    expect(await screen.findByTestId('ignore-no-matches')).toBeInTheDocument()
  })

  it('warns that tracked files will not be hidden', async () => {
    mockApi({ tracked: ['docs/notes.md'] })
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'docs/notes.md', isFolder: false }}
        onClose={vi.fn()}
      />,
    )

    const warning = await screen.findByTestId('ignore-tracked-warning')
    expect(warning).toHaveTextContent(/1 tracked file/)
    expect(warning).toHaveTextContent(/stay visible/)
  })

  it('writes the edited line, not the generated one', async () => {
    mockApi()
    const onClose = vi.fn()
    const user = userEvent.setup()
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'bin-check', isFolder: true }}
        onClose={onClose}
      />,
    )
    const input = await screen.findByTestId('ignore-pattern-input')
    await user.clear(input)
    await user.type(input, '*.dll')

    await user.click(screen.getByRole('button', { name: /add to .gitignore/i }))

    await waitFor(() => expect(addRequests).toEqual(['*.dll']))
    expect(onClose).toHaveBeenCalled()
  })

  it('surfaces a server refusal instead of closing', async () => {
    server.use(
      http.post(`*/api/agents/${AGENT}/files/ignore/preview`, () =>
        HttpResponse.json({ pattern: 'bin-check/', matches: [], truncated: false, trackedMatches: [] }),
      ),
      http.post(`*/api/agents/${AGENT}/files/ignore`, () =>
        HttpResponse.json({ detail: 'Workspace is not a git repository.' }, { status: 404 }),
      ),
    )
    const onClose = vi.fn()
    const user = userEvent.setup()
    renderWithProviders(
      <IgnorePathModal
        agentId={AGENT}
        target={{ path: 'bin-check', isFolder: true }}
        onClose={onClose}
      />,
    )
    await screen.findByTestId('ignore-pattern-input')

    await user.click(screen.getByRole('button', { name: /add to .gitignore/i }))

    await waitFor(() => {
      expect(screen.getByText(/not a git repository/i)).toBeInTheDocument()
    })
    expect(onClose).not.toHaveBeenCalled()
  })

  it('renders nothing until a target is chosen', () => {
    renderWithProviders(<IgnorePathModal agentId={AGENT} target={null} onClose={vi.fn()} />)
    expect(screen.queryByTestId('ignore-pattern-input')).not.toBeInTheDocument()
  })
})
