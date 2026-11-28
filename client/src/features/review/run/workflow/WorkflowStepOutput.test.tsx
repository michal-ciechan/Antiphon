import { MantineProvider } from '@mantine/core'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { NodeExecutionDto, NodeStatus, WorkflowNodeDto } from '../../../../api/reviewEngine'
import { NodeSidePanel } from './NodeSidePanel'
import { StepNodeView } from './WorkflowNodes'
import deltaOutput from './delta-output.fixture.json'

vi.mock('@xyflow/react', async (importOriginal) => ({
  ...await importOriginal<typeof import('@xyflow/react')>(),
  Handle: () => null,
}))

const def: WorkflowNodeDto = {
  id: 'categorise',
  kind: 'Decision',
  run: 'Categorise',
  description: 'Categorise the change.',
  params: { onFailure: 'continue', fallback: 'default' },
}

function execution(overrides: Partial<NodeExecutionDto> = {}): NodeExecutionDto {
  return {
    id: 'execution-1',
    nodeId: def.id,
    iteration: 1,
    kind: def.kind,
    status: 'Succeeded',
    itemKey: null,
    outcome: 'feature',
    provider: null,
    model: null,
    transport: null,
    inputTokens: 0,
    outputTokens: 0,
    costUsd: 0,
    startedAt: null,
    completedAt: null,
    summary: null,
    ...overrides,
  }
}

function renderInTheme(view: ReactNode) {
  return render(<MantineProvider forceColorScheme="dark">{view}</MantineProvider>)
}

function renderPanel(e: NodeExecutionDto) {
  return renderInTheme(<NodeSidePanel def={def} execs={[e]} iteration={1} now={0} />)
}

function renderCard(e: NodeExecutionDto) {
  return renderInTheme(
    <StepNodeView
      id={def.id}
      type="step"
      data={{
        def, status: e.status, execs: [e], items: [], iterationsRun: 1, dir: 'ltr', selected: false,
        item: null, expandable: false, expanded: false, agentCount: 0,
      }}
      positionAbsoluteX={0}
      positionAbsoluteY={0}
      isConnectable={false}
      dragging={false}
      zIndex={0}
      selectable
      deletable={false}
      selected={false}
      draggable={false}
    />,
  )
}

describe('Workflow execution output', () => {
  it('preserves line breaks and indentation in a wrapping monospace block', () => {
    const summary = 'Changed files\n\n  M src/one.ts (+29 -0)\n  A src/two.ts (+8 -0)'
    const { container } = renderPanel(execution({ summary }))

    const output = container.querySelector('pre')
    expect(output).toBeInTheDocument()
    expect(output?.textContent).toBe(summary)
    expect(output).toHaveStyle({ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' })
    expect(output).toHaveStyle({ fontFamily: 'var(--mantine-font-family-monospace)' })
  })

  it('renders only the producer warnings and keeps the file list and routing failure in output', () => {
    const failure = "No edge leaves 'categorise' for outcome 'feature'."
    const output = { ...deltaOutput, summary: `${deltaOutput.summary}\n${failure}` }
    const { container } = renderPanel(execution({ status: 'Failed', ...output }))

    expect(container.querySelector('pre')?.textContent).toBe(output.summary)
    const warnings = within(screen.getByRole('list', { name: 'Warnings' })).getAllByRole('listitem')
    expect(warnings.map((warning) => warning.textContent)).toEqual(deltaOutput.warnings)
  })

  it('preserves ambiguous legacy output as literal text', () => {
    const legacy = `${deltaOutput.summary}\n${deltaOutput.warnings[0]}\n<img src=x><b>literal</b>`
    const { container } = renderPanel(execution({ summary: legacy }))

    expect(container.querySelector('pre')?.textContent).toBe(legacy)
    expect(container.querySelector('pre img, pre b')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Warnings' })).not.toBeInTheDocument()
  })

  it('declares theme-aware output and keyboard focus colors', () => {
    const { container } = renderPanel(execution(deltaOutput))

    expect(container.querySelector('pre')?.parentElement).toHaveStyle({
      background: 'var(--mantine-color-default)',
      color: 'var(--mantine-color-text)',
    })
    const region = screen.getByRole('region', { name: 'Execution history' })
    expect(region.className).toMatch(/executionHistory/i)
    expect(region).toHaveStyle({ '--execution-history-focus-color': 'var(--mantine-color-text)' })
  })

  it('loads the rest of a large summary on demand one page at a time', async () => {
    const prefix = 'large summary'
    const summary = prefix + 'x'.repeat(1024 * 1024) + '<b>last page</b>'
    const { container } = renderPanel(execution({ summary }))
    const pre = () => container.querySelector('pre')

    expect(pre()?.textContent?.startsWith(prefix)).toBe(true)
    expect(pre()?.textContent?.length).toBeLessThanOrEqual(4096)
    expect(screen.queryByRole('button', { name: 'Next page' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Show full output' }))
    expect(pre()?.textContent?.length).toBeLessThanOrEqual(4096)
    await userEvent.click(screen.getByRole('button', { name: 'Last page' }))
    expect(pre()?.textContent?.endsWith('<b>last page</b>')).toBe(true)
    expect(pre()?.querySelector('b')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Show preview' }))
    expect(pre()?.textContent?.startsWith(prefix)).toBe(true)
  })

  it('keeps wide execution rows in a keyboard-focusable horizontal scroll region', async () => {
    const outcome = 'feature-with-a-wide-decision-outcome'
    renderPanel(execution({
      outcome, itemKey: 'a-wide-reviewer-group-name', model: 'claude-opus-4-7', inputTokens: 10000, outputTokens: 2000,
    }))

    const region = screen.getByRole('region', { name: 'Execution history' })
    expect(region).toHaveStyle({ overflowX: 'auto', maxWidth: '100%' })
    const table = within(region).getByRole('table')
    expect(table).toHaveStyle({ whiteSpace: 'nowrap' })
    expect(within(table).getByRole('columnheader', { name: 'Time' })).toBeVisible()
    expect(within(table).getByText(`→ ${outcome}`)).toBeVisible()
    await userEvent.tab()
    expect(region).toHaveFocus()
  })
})

describe('Workflow graph outcome', () => {
  it.each([
    ['Succeeded', 'Succeeded', 'feature', 'feature'],
    ['Failed', 'Failed', 'feature', null],
    ['Skipped', 'Skipped', null, null],
    ['FailedContinued', 'Failed, continued', 'affected', 'affected'],
  ] as const)('shows the recorded %s outcome and its route', (status: NodeStatus, label, outcome, route) => {
    renderCard(execution({ status, outcome }))

    expect(screen.getByText(label, { exact: true })).toBeVisible()
    if (route) expect(screen.getByText(route, { exact: true })).toBeVisible()
    else if (outcome) expect(screen.queryByText(outcome, { exact: true })).not.toBeInTheDocument()
  })

  it('shows the first output line of a successful step without a labelled route', () => {
    renderCard(execution({ outcome: null, summary: '2 kept, 2 disproved\nMore detail' }))

    expect(screen.getByText('2 kept, 2 disproved')).toBeVisible()
    expect(screen.queryByText('More detail')).not.toBeInTheDocument()
  })

  it('bounds the displayed and titled preview of a long first line', () => {
    const prefix = 'large summary'
    renderCard(execution({ outcome: null, summary: prefix + 'x'.repeat(1024 * 1024) }))

    const preview = screen.getByText(new RegExp(`^${prefix}`))
    expect(preview.textContent?.length).toBeLessThanOrEqual(160)
    expect(preview.getAttribute('title')?.length ?? 0).toBeLessThanOrEqual(160)
  })
})
