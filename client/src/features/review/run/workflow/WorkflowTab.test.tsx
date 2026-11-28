import { createElement, useEffect, type ComponentProps, type ComponentType } from 'react'
import { HttpResponse, http } from 'msw'
import { Link, Route, Routes, useParams } from 'react-router'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { act, renderWithProviders, screen, settleQueries, userEvent, waitFor, within } from '../../../../test/utils'
import { server } from '../../../../test/mocks/server'
import { reviewEngineKeys, type ReviewRunDetailDto, type WorkflowPolicyDto, type WorkflowValidationDto } from '../../../../api/reviewEngine'
import { createSeed } from '../../fake/seed'
import { DEFAULT_WORKFLOW } from '../../fake/workflowSeed'
import { forceReviewApiMode } from '../../reviewApi'
import { useReviewUiStore } from '../../useReviewUiStore'
import { WorkflowTab } from './WorkflowTab'

const { fitView } = vi.hoisted(() => ({ fitView: vi.fn(async () => true) }))

vi.mock('@xyflow/react', async (importOriginal) => {
  const original = await importOriginal<typeof import('@xyflow/react')>()
  type CanvasProps = ComponentProps<typeof original.ReactFlow>
  function Canvas({ nodes, edges, nodeTypes, onInit, onNodeClick }: CanvasProps) {
    useEffect(() => {
      onInit?.({ fitView } as unknown as import('@xyflow/react').ReactFlowInstance)
    }, [onInit])
    return (
      <div>
        {nodes?.map((node) => {
          const View = nodeTypes?.[node.type ?? ''] as ComponentType<{ data: unknown }> | undefined
          return (
            <div key={node.id} data-testid={'graph-node-' + node.id}>
              {View && createElement(View, { data: node.data })}
              {node.type === 'step' && (
                <button type="button" onClick={(event) => onNodeClick?.(event, node)}>Select {node.id}</button>
              )}
            </div>
          )
        })}
        {edges?.map((edge) => (
          <span key={edge.id} data-testid="graph-edge">{edge.label ?? String(edge.data?.label ?? '')}</span>
        ))}
      </div>
    )
  }

  return { ...original, ReactFlow: Canvas, Background: () => null, Controls: () => null, Handle: () => null }
})

const now = Date.parse('2026-11-27T12:00:00Z')
const graph = {
  ...DEFAULT_WORKFLOW,
  nodes: DEFAULT_WORKFLOW.nodes.map((node) => node.id === 'human-approve'
    ? { ...node, params: { ...node.params, optional: true } }
    : node),
}
const [first, second] = createSeed(now).runs.map((run) => ({ ...run, workflowRevisionId: graph.revisionId }))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((settle) => { resolve = settle })
  return { promise, resolve }
}

function policy(run: ReviewRunDetailDto, gatesOff: boolean): WorkflowPolicyDto {
  return {
    runId: run.id,
    revisionId: graph.revisionId,
    name: graph.name,
    hash: graph.hash,
    nodes: [{
      nodeId: 'human-approve',
      onFailure: null,
      chain: null,
      gatesOff: { value: gatesOff, source: gatesOff ? 'RepoRule' : 'StepDefault' },
    }],
  }
}

function workflowRoute(run: ReviewRunDetailDto) {
  return '/reviews/' + run.id + '?tab=workflow'
}

function FixtureRoute({ runs }: { runs: ReviewRunDetailDto[] }) {
  const { runId } = useParams()
  const run = runs.find((candidate) => candidate.id === runId)!
  const other = runs.find((candidate) => candidate.id !== run.id)
  return (
    <>
      {other && <Link to={workflowRoute(other)}>Open other run</Link>}
      <WorkflowTab run={run} now={now} />
    </>
  )
}

function renderWorkflow(...runs: ReviewRunDetailDto[]) {
  window.history.replaceState(null, '', workflowRoute(runs[0]))
  return renderWithProviders(
    <Routes>
      <Route path="/reviews/:runId" element={<FixtureRoute runs={runs} />} />
    </Routes>,
  )
}

async function selectGate(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Select human-approve' }))
}

beforeEach(() => {
  forceReviewApiMode('http')
  useReviewUiStore.setState({ sectionCollapse: {} })
  fitView.mockClear()
  server.use(
    http.get('/api/review-workflows/revisions/' + graph.revisionId, () => HttpResponse.json(graph)),
    http.get('/api/review-runs/:id/workflow-policy', ({ params }) =>
      HttpResponse.json(policy(params.id === first.id ? first : second, false))),
  )
})

afterEach(() => {
  forceReviewApiMode(null)
  useReviewUiStore.setState({ sectionCollapse: {} })
})

test('C2109_S1_diagnostic_focus', async () => {
  const edgeIndex = graph.edges.findIndex((edge) => edge.from === 'await-merge' && edge.to === 'base-drift')
  expect(edgeIndex).toBeGreaterThanOrEqual(0)
  const diagnostics: WorkflowValidationDto = {
    valid: false,
    diagnostics: [
      { severity: 'Error', code: 'unknown-chain', message: 'Choose a valid chain.', nodeIds: ['base-drift'], edge: null },
      { severity: 'Warning', code: 'edge-warning', message: 'Check this route.', nodeIds: [], edge: { from: 'await-merge', to: 'base-drift', index: edgeIndex } },
      { severity: 'Warning', code: 'graph-wide', message: 'Graph-wide note.', nodeIds: [], edge: null },
    ],
  }
  server.use(http.post('/api/review-workflows/validate', () => HttpResponse.json(diagnostics)))
  const { queryClient } = renderWorkflow(first)
  const user = userEvent.setup()
  await settleQueries(queryClient)
  expect(screen.queryByTestId('graph-node-base-drift')).not.toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Validate' }))
  await settleQueries(queryClient)
  expect(screen.getByText(/Graph-wide note/)).toBeInTheDocument()

  screen.getByRole('button', { name: 'await-merge → base-drift' }).focus()
  await user.keyboard('{Enter}')
  await waitFor(() => expect(screen.getByRole('heading', { name: 'base-drift' })).toHaveFocus())
  expect(within(screen.getByTestId('graph-node-base-drift')).getByLabelText('1 error')).toBeVisible()
  expect(screen.getAllByTestId('graph-edge').filter((element) => element.textContent?.includes('1 warning'))).toHaveLength(1)
  await waitFor(() => expect(fitView).toHaveBeenCalledWith(expect.objectContaining({
    nodes: expect.arrayContaining([{ id: 'await-merge' }, { id: 'base-drift' }]),
  })))

  const toggle = screen.getByRole('button', { name: 'Intake' })
  expect(screen.getByTestId('graph-node-intake')).toBeVisible()
  toggle.focus()
  await user.keyboard('{Enter}')
  expect(screen.queryByTestId('graph-node-intake')).not.toBeInTheDocument()
  await act(async () => {
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
  })
  expect(toggle).toHaveFocus()
})

test('C2109_S1_policy_identity', async () => {
  const firstStarted = deferred<void>()
  const lateFirst = deferred<WorkflowPolicyDto>()
  const secondStarted = deferred<void>()
  const secondPolicy = deferred<WorkflowPolicyDto>()
  let firstReads = 0
  server.use(http.get('/api/review-runs/:id/workflow-policy', async ({ params, request }) => {
    expect(new URL(request.url).searchParams.get('revisionId')).toBe(graph.revisionId)
    if (params.id === first.id) {
      if (firstReads++ === 0) return HttpResponse.json(policy(first, true))
      firstStarted.resolve()
      return HttpResponse.json(await lateFirst.promise)
    }
    secondStarted.resolve()
    return HttpResponse.json(await secondPolicy.promise)
  }))
  const { queryClient } = renderWorkflow(first, second)
  const user = userEvent.setup()
  await settleQueries(queryClient)
  await selectGate(user)
  expect(screen.getByText('Gate · off · Repository rule')).toBeVisible()

  try {
    void queryClient.invalidateQueries({ queryKey: reviewEngineKeys.workflowPolicy(first.id, graph.revisionId) })
    await firstStarted.promise
    await user.click(screen.getByRole('link', { name: 'Open other run' }))
    await selectGate(user)
    expect(screen.getByText('Loading current server policy…')).toBeVisible()
    expect(screen.queryByText('Gate · off · Repository rule')).not.toBeInTheDocument()
    await secondStarted.promise
    await act(async () => secondPolicy.resolve(policy(second, false)))
    expect(await screen.findByText('Gate · on · Step default')).toBeVisible()
    expect(screen.getByTitle(graph.hash)).toHaveTextContent('pr-review · 9c41e2b07d3a')
  } finally {
    secondPolicy.resolve(policy(second, false))
    lateFirst.resolve(policy(first, true))
    await settleQueries(queryClient)
  }

  expect(screen.getByText('Gate · on · Step default')).toBeVisible()
  expect(screen.queryByText('Gate · off · Repository rule')).not.toBeInTheDocument()
})

test('C2109_S1_no_policy_for_missing_revision', async () => {
  const missing = { ...second, workflowRevisionId: 'missing-revision' }
  const missingPolicyCalls = vi.fn()
  server.use(
    http.get('/api/review-workflows/revisions/missing-revision', () => new HttpResponse(null, { status: 404 })),
    http.get('/api/review-runs/' + missing.id + '/workflow-policy', () => {
      missingPolicyCalls()
      return HttpResponse.json(policy(missing, true))
    }),
  )
  const { queryClient } = renderWorkflow(first, missing)
  const user = userEvent.setup()
  await settleQueries(queryClient)
  await selectGate(user)
  expect(screen.getByText('Gate · on · Step default')).toBeVisible()
  await user.click(screen.getByRole('link', { name: 'Open other run' }))
  await settleQueries(queryClient)
  expect(screen.getByText('Could not load the workflow')).toBeVisible()
  expect(screen.queryByText('Effective policy')).not.toBeInTheDocument()
  expect(missingPolicyCalls).not.toHaveBeenCalled()
})
