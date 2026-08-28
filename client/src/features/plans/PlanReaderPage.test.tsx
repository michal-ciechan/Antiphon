import { HttpResponse, http } from 'msw'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, userEvent } from '../../test/utils'
import { server } from '../../test/mocks/server'
import type { PlanSummaryDto } from '../../api/plans'
import { PlanReaderPage } from './PlanReaderPage'

const SPEC: PlanSummaryDto = {
  relativePath: 'docs/superpowers/specs/2026-08-16-card-0035-stuck-work-view.md',
  fileName: '2026-08-16-card-0035-stuck-work-view.md',
  kind: 'Spec',
  title: 'Stuck-work view',
  date: '2026-08-16',
  status: 'Planned',
  cards: ['CARD-0035'],
  mentionedCards: ['CARD-0002'],
  sizeBytes: 4321,
  modifiedAt: '2026-08-16T12:00:00Z',
}

// A proposal carries neither date nor status — the contract says so, and the page must not assume.
const PROPOSAL: PlanSummaryDto = {
  relativePath: 'docs/features/013-review/proposal.md',
  fileName: 'proposal.md',
  kind: 'Proposal',
  title: 'Review flow',
  date: null,
  status: null,
  cards: [],
  mentionedCards: [],
  sizeBytes: 900,
  modifiedAt: '2026-08-10T09:00:00Z',
}

const CONTENT = `# Stuck-work view

## Decisions

nine conditions, one projection

## Server design

three new files`

function seedCatalog(plans: PlanSummaryDto[] = [SPEC, PROPOSAL]) {
  server.use(
    http.get('/api/plans', () =>
      HttpResponse.json({
        root: 'C:/src/Antiphon',
        rootResolved: true,
        generatedAt: '2026-08-17T12:00:00Z',
        plans,
      }),
    ),
  )
}

function seedContent(content: string = CONTENT, plan: PlanSummaryDto = SPEC) {
  server.use(
    http.get('/api/plans/content', () => HttpResponse.json({ plan, content })),
  )
}

function renderPlansRoute(initialPath: string) {
  window.history.pushState({}, '', initialPath)
  return renderWithProviders(
    <Routes>
      <Route path="/plans" element={<PlanReaderPage />} />
    </Routes>,
  )
}

const READER_URL = `/plans?file=${encodeURIComponent(SPEC.relativePath)}`

describe('PlanReaderPage', () => {
  it('lists the catalog: card as #n, title, status; a proposal shows its kind and no date', async () => {
    seedCatalog()
    renderPlansRoute('/plans')

    expect(await screen.findByText('Stuck-work view')).toBeInTheDocument()
    expect(screen.getByText('#35')).toBeInTheDocument()
    expect(screen.getByText('Planned')).toBeInTheDocument()
    expect(screen.getByText('2026-08-16')).toBeInTheDocument()

    const proposalRow = screen.getByTestId('plan-row-proposal.md')
    expect(proposalRow).toHaveTextContent('Review flow')
    expect(proposalRow).toHaveTextContent('proposal')
  })

  it('tapping a catalog row opens that plan in the reader', async () => {
    seedCatalog()
    seedContent()
    renderPlansRoute('/plans')

    await userEvent.click(await screen.findByTestId(`plan-row-${SPEC.fileName}`))

    expect(await screen.findByTestId('plan-header')).toBeInTheDocument()
    expect(window.location.search).toContain('file=')
  })

  it('reads ToC-first: section headings listed, no body rendered until a tap', async () => {
    seedContent()
    renderPlansRoute(READER_URL)

    expect(await screen.findByText('Decisions')).toBeInTheDocument()
    expect(screen.getByText('Server design')).toBeInTheDocument()
    expect(screen.queryByText(/nine conditions/)).not.toBeInTheDocument()
    expect(screen.queryByText(/three new files/)).not.toBeInTheDocument()
  })

  it('loads only the task named in the URL, never the task collection', async () => {
    const requested: string[] = []
    server.use(
      http.get('/api/agent-tasks/:id', ({ params }) => {
        requested.push(`/api/agent-tasks/${params.id}`)
        return HttpResponse.json({ summary: { id: params.id, role: 'Plan' } })
      }),
      http.get('/api/agent-tasks', () => {
        requested.push('/api/agent-tasks')
        return HttpResponse.json([])
      }),
      http.post('/api/agent-tasks/:id/read', () => HttpResponse.json({})),
    )
    seedCatalog()
    seedContent()
    renderPlansRoute(`${READER_URL}&task=12345678-1234-1234-1234-123456789abc`)

    await screen.findByTestId('plan-header')
    expect(requested).toContain('/api/agent-tasks/12345678-1234-1234-1234-123456789abc')
    expect(requested).not.toContain('/api/agent-tasks')
  })

  it('a section expands in place and collapses again', async () => {
    seedContent()
    renderPlansRoute(READER_URL)

    await userEvent.click(await screen.findByRole('button', { name: 'Expand section decisions' }))
    expect(screen.getByTestId('plan-section-body-decisions')).toHaveTextContent(
      'nine conditions, one projection',
    )
    // The rest of the plan stays one tap away — the neighbour did not open with it.
    expect(screen.queryByText(/three new files/)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Collapse section decisions' }))
    expect(screen.queryByText(/nine conditions/)).not.toBeInTheDocument()
  })

  it('the sticky header carries #n, title, status, path and date', async () => {
    seedContent()
    renderPlansRoute(READER_URL)

    const header = await screen.findByTestId('plan-header')
    expect(header).toHaveTextContent('#35')
    expect(header).toHaveTextContent('Stuck-work view')
    expect(header).toHaveTextContent('Planned')
    expect(header).toHaveTextContent(SPEC.relativePath)
    expect(header).toHaveTextContent('2026-08-16')
  })

  it('a 422 renders as a refusal, with the server refusal text, distinct from not-found', async () => {
    server.use(
      http.get('/api/plans/content', () =>
        HttpResponse.json(
          { title: 'Validation failed', errors: { file: ['is outside the plan directories'] } },
          { status: 422 },
        ),
      ),
    )
    renderPlansRoute(READER_URL)

    const refused = await screen.findByTestId('plan-error-refused')
    expect(refused).toHaveTextContent('is outside the plan directories')
    expect(screen.queryByTestId('plan-error-notfound')).not.toBeInTheDocument()
  })

  it('a 404 renders as not-found, distinct from a refusal', async () => {
    server.use(
      http.get('/api/plans/content', () =>
        HttpResponse.json(
          { title: 'Not Found', detail: "Plan 'nope.md' was not found." },
          { status: 404 },
        ),
      ),
    )
    renderPlansRoute(READER_URL)

    expect(await screen.findByTestId('plan-error-notfound')).toHaveTextContent('was not found')
    expect(screen.queryByTestId('plan-error-refused')).not.toBeInTheDocument()
  })

  it('an unresolved root reads as absent, not as an empty catalog', async () => {
    server.use(
      http.get('/api/plans', () =>
        HttpResponse.json({
          root: null,
          rootResolved: false,
          generatedAt: '2026-08-17T12:00:00Z',
          plans: [],
        }),
      ),
    )
    renderPlansRoute('/plans')

    expect(await screen.findByTestId('plans-no-root')).toHaveTextContent('absent, not empty')
    expect(screen.queryByText('No plan files found.')).not.toBeInTheDocument()
  })
})
