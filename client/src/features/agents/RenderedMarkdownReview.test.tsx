import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { RenderedMarkdownReview } from './RenderedMarkdownReview'
import { sectionHash } from './markdownSections'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
vi.setConfig({ testTimeout: 20_000 })

const AGENT = 'a0000000-0000-0000-0000-000000000001'
const PATH = 'docs/plan.md'

const WORK = `# Title

opening

## Setup

install steps

## Deploy

ship it`

function hashOf(section: string): string {
  return sectionHash(section.trimEnd())
}

const SETUP_HASH = hashOf('## Setup\n\ninstall steps\n')
const DEPLOY_HASH = hashOf('## Deploy\n\nship it')

interface MarkBody {
  path: string
  sections: Array<{ key: string; contentHash: string | null }>
}

function seed(marks: Array<{ key: string; contentHash: string }>) {
  const captured: { body: MarkBody | null } = { body: null }
  server.use(
    http.get(`/api/agents/${AGENT}/review/sections`, () =>
      HttpResponse.json(marks.map((m) => ({ ...m, updatedAt: '2026-08-08T00:00:00Z' }))),
    ),
    http.post(`/api/agents/${AGENT}/review/sections`, async ({ request }) => {
      captured.body = (await request.json()) as MarkBody
      return HttpResponse.json({ marked: captured.body.sections.length })
    }),
  )
  return captured
}

function render(overrides: Partial<Parameters<typeof RenderedMarkdownReview>[0]> = {}) {
  return renderWithProviders(
    <RenderedMarkdownReview
      agentId={AGENT}
      path={PATH}
      workText={WORK}
      baseText={null}
      threads={[]}
      onCommentAtLine={() => {}}
      {...overrides}
    />,
  )
}

describe('RenderedMarkdownReview', () => {
  it('renders every section expanded when nothing is reviewed', async () => {
    seed([])
    render()
    await waitFor(() => expect(screen.getByTestId('section-body-setup')).toBeInTheDocument())
    expect(screen.getByTestId('section-body-deploy')).toHaveTextContent('ship it')
  })

  it('a reviewed-and-unchanged section auto-collapses; expanding it back is one click', async () => {
    seed([{ key: 'setup', contentHash: SETUP_HASH }])
    render()

    await waitFor(() =>
      expect(screen.queryByTestId('section-body-setup')).not.toBeInTheDocument(),
    )
    const row = screen.getByTestId('section-setup')
    expect(within(row).getByText(/reviewed · collapsed/)).toBeInTheDocument()
    // Deploy is unreviewed — stays expanded.
    expect(screen.getByTestId('section-body-deploy')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Expand section setup' }))
    expect(screen.getByTestId('section-body-setup')).toBeInTheDocument()
  })

  it('a section changed since its mark shows the stale badge and stays expanded', async () => {
    seed([{ key: 'setup', contentHash: 'stale-old-hash' }])
    render()

    await waitFor(() => expect(screen.getByTestId('stale-setup')).toBeInTheDocument())
    expect(screen.getByTestId('section-body-setup')).toBeInTheDocument()
  })

  it('marking a section posts the subtree batch with current hashes', async () => {
    const captured = seed([])
    render()
    await waitFor(() => expect(screen.getByTestId('section-body-title')).toBeInTheDocument())

    // Title is an h1 — its subtree is the whole document below it.
    await userEvent.click(screen.getByRole('button', { name: 'Mark section title reviewed' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body!.path).toBe(PATH)
    expect(captured.body!.sections.map((s) => s.key)).toEqual(['title', 'setup', 'deploy'])
    expect(captured.body!.sections.every((s) => s.contentHash !== null)).toBe(true)
    expect(captured.body!.sections.find((s) => s.key === 'deploy')!.contentHash).toBe(DEPLOY_HASH)
  })

  it('clearing a reviewed section posts null hashes for its subtree', async () => {
    const captured = seed([{ key: 'deploy', contentHash: DEPLOY_HASH }])
    render()
    // Wait for the marks to load (deploy auto-collapses) — clicking the pre-fetch render's
    // button would dispatch onto an unmounted node.
    await waitFor(() =>
      expect(screen.queryByTestId('section-body-deploy')).not.toBeInTheDocument(),
    )

    await userEvent.click(screen.getByRole('button', { name: 'Mark section deploy reviewed' }))

    await waitFor(() => expect(captured.body).not.toBeNull())
    expect(captured.body!.sections).toEqual([{ key: 'deploy', contentHash: null }])
  })

  it('the comment control anchors at the section heading line', async () => {
    seed([])
    const lines: number[] = []
    render({ onCommentAtLine: (line) => lines.push(line) })
    await waitFor(() => expect(screen.getByTestId('section-body-setup')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Comment on section setup' }))
    expect(lines).toEqual([5]) // "## Setup" is line 5 of the document
  })

  it('inline diff shows changed blocks and removed sections at their old position', async () => {
    seed([])
    const base = `# Title

opening

## Setup

old install steps

## Removed chapter

gone now

## Deploy

ship it`
    render({ baseText: base })

    await userEvent.click(screen.getByRole('radio', { name: 'Inline diff' }))

    const changed = await screen.findByTestId('changed-setup')
    expect(changed).toHaveTextContent('old install steps')
    expect(changed).toHaveTextContent('install steps')
    expect(screen.getByTestId('removed-removed-chapter')).toHaveTextContent('gone now')
  })

  it('side-by-side renders base and work cells per section', async () => {
    seed([])
    render({ baseText: WORK.replace('install steps', 'previous steps') })

    await userEvent.click(screen.getByRole('radio', { name: 'Side by side' }))

    const row = await screen.findByTestId('side-setup')
    expect(row).toHaveTextContent('previous steps')
    expect(row).toHaveTextContent('install steps')
  })

  it('read-only (context files) hides the mark control but keeps comments', async () => {
    seed([])
    render({ readOnly: true })
    await waitFor(() => expect(screen.getByTestId('section-body-setup')).toBeInTheDocument())

    expect(
      screen.queryByRole('button', { name: 'Mark section setup reviewed' }),
    ).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Comment on section setup' })).toBeInTheDocument()
  })
})
