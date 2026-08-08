import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { HomePage } from './HomePage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))
vi.setConfig({ testTimeout: 20_000 })

// The heavy panes are integration-tested on their own; here they only need to prove they were
// composed with the right identity.
vi.mock('../agents/FilesReviewPanel', () => ({
  FilesReviewPanel: ({ agentId }: { agentId: string }) => (
    <div data-testid="files-panel">{agentId}</div>
  ),
}))
vi.mock('../agents/SessionTranscriptPanel', () => ({
  SessionTranscriptPanel: ({ sessionId }: { sessionId: string }) => (
    <div data-testid="chat-panel">{sessionId}</div>
  ),
}))

function agent(overrides: Record<string, unknown>) {
  return {
    id: 'a1',
    name: 'axc',
    slug: 'axc',
    workingDirectory: 'C:\\src\\antiphon',
    details: '',
    defaultWorkflowTemplateId: null,
    defaultWorkflowTemplateName: null,
    assignmentPolicy: 'AutoPick',
    status: 'Idle',
    persistentSessionId: null,
    currentCardId: null,
    boardId: null,
    boardName: null,
    queueLength: 0,
    createdAt: '2026-08-08T00:00:00Z',
    updatedAt: '2026-08-08T00:00:00Z',
    liveSession: null,
    alwaysOn: false,
    remoteControlEnabled: false,
    supervision: null,
    systemPromptAppend: null,
    modelLevel: 'High',
    working: false,
    ...overrides,
  }
}

function task(overrides: Record<string, unknown>) {
  return {
    id: '11111111-0000-0000-0000-000000000001',
    rootTaskId: '11111111-0000-0000-0000-000000000001',
    parentTaskId: null,
    depth: 0,
    title: 'tighten the deploy doc',
    kind: 'Worker',
    role: 'Docs',
    modelLevel: 'Medium',
    escalatedFrom: null,
    status: 'Working',
    workspace: 'Shared',
    workingDirectory: 'C:\\src\\antiphon',
    repoPath: null,
    scopeGlob: null,
    agentId: null,
    agentName: 'pool-1',
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-08T10:00:00Z',
    dispatchedAt: '2026-08-08T10:00:05Z',
    completedAt: null,
    tokensIn: 0,
    tokensOut: 0,
    costUsd: 0.42,
    subtreeCostUsd: 0.42,
    childCount: 0,
    ...overrides,
  }
}

function seed({
  agents = [agent({})],
  tasks = [] as unknown[],
} = {}) {
  server.use(
    http.get('/api/agents', () => HttpResponse.json(agents)),
    http.get('/api/agent-tasks', () => HttpResponse.json(tasks)),
  )
}

beforeEach(() => {
  window.localStorage.clear()
})

describe('HomePage', () => {
  it('shows the project, its agents, and the files pane for the picked agent', async () => {
    seed({
      agents: [
        agent({ id: 'a1', name: 'axc' }),
        agent({ id: 'a2', name: 'pool-1', liveSession: { id: 's2', status: 'Running' } }),
      ],
    })
    renderWithProviders(<HomePage />)

    await waitFor(() =>
      expect(screen.getByTestId('project-switcher')).toHaveTextContent('antiphon'),
    )
    // A live session beats a cold agent for the default selection.
    expect(screen.getByTestId('files-panel')).toHaveTextContent('a2')
    expect(screen.getByRole('button', { name: 'Select agent axc' })).toBeInTheDocument()
  })

  it('selecting an agent in the rail redirects the files pane and the chat dock', async () => {
    seed({
      agents: [
        agent({ id: 'a1', name: 'axc', persistentSessionId: 'session-a1' }),
        agent({ id: 'a2', name: 'pool-1', liveSession: { id: 's2', status: 'Running' } }),
      ],
    })
    renderWithProviders(<HomePage />)

    await waitFor(() => expect(screen.getByTestId('files-panel')).toHaveTextContent('a2'))
    await userEvent.click(screen.getByRole('button', { name: 'Select agent axc' }))

    expect(screen.getByTestId('files-panel')).toHaveTextContent('a1')
    expect(screen.getByTestId('chat-panel')).toHaveTextContent('session-a1')
  })

  it('the Tasks tab lists only this project’s delegations — worktree tasks match via their repo', async () => {
    seed({
      agents: [agent({})],
      tasks: [
        task({}),
        task({
          id: '11111111-0000-0000-0000-000000000002',
          title: 'merge-back from a worktree',
          status: 'Succeeded',
          workspace: 'Worktree',
          workingDirectory: 'C:\\wt\\task-abc',
          repoPath: 'C:\\src\\antiphon',
          completedAt: '2026-08-08T11:00:00Z',
        }),
        task({
          id: '11111111-0000-0000-0000-000000000003',
          title: 'other project task',
          workingDirectory: 'C:\\src\\am-service',
        }),
      ],
    })
    renderWithProviders(<HomePage />)

    // The other-project directory appears in the switcher, but its tasks stay out of this panel.
    await waitFor(() => expect(screen.getByTestId('project-switcher')).toHaveTextContent('antiphon'))
    await userEvent.click(screen.getByRole('tab', { name: 'Tasks' }))

    const dock = screen.getByTestId('home-dock')
    expect(within(dock).getByText('tighten the deploy doc')).toBeInTheDocument()
    expect(within(dock).getByText('merge-back from a worktree')).toBeInTheDocument()
    expect(within(dock).queryByText('other project task')).not.toBeInTheDocument()
  })

  it('with no agents anywhere it still offers the two ways to start', async () => {
    seed({ agents: [] })
    renderWithProviders(<HomePage />)

    await waitFor(() => expect(screen.getByText('No agents and no work yet.')).toBeInTheDocument())
    // Header button + empty-state anchor both offer it — at least one path in.
    expect(screen.getAllByText('Delegate work').length).toBeGreaterThan(0)
    // The rail and the files pane both point at agent creation — either is fine, both expected.
    expect(screen.getAllByRole('link', { name: 'create an agent' }).length).toBeGreaterThan(0)
  })

  it('remembers the chosen project across mounts', async () => {
    seed({
      agents: [
        agent({ id: 'a1', workingDirectory: 'C:\\src\\antiphon' }),
        agent({ id: 'a2', name: 'gateway', workingDirectory: 'C:\\src\\am-service' }),
      ],
    })
    const first = renderWithProviders(<HomePage />)
    // am-service sorts first, so it is the default.
    await waitFor(() =>
      expect(screen.getByTestId('project-switcher')).toHaveTextContent('am-service'),
    )
    await userEvent.click(screen.getByTestId('project-switcher'))
    await userEvent.click(await screen.findByText('antiphon'))
    await waitFor(() =>
      expect(screen.getByTestId('project-switcher')).toHaveTextContent('antiphon'),
    )
    first.unmount()

    seed({
      agents: [
        agent({ id: 'a1', workingDirectory: 'C:\\src\\antiphon' }),
        agent({ id: 'a2', name: 'gateway', workingDirectory: 'C:\\src\\am-service' }),
      ],
    })
    renderWithProviders(<HomePage />)
    await waitFor(() =>
      expect(screen.getByTestId('project-switcher')).toHaveTextContent('antiphon'),
    )
  })
})
