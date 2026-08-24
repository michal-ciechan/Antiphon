import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { HomePage } from './HomePage'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

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
    worktreePath: null,
    worktreeBranch: null,
    scopeGlob: null,
    agentId: null,
    agentName: 'pool-1',
    agentSessionId: null,
    attempt: 1,
    createdAt: '2026-08-08T10:00:00Z',
    dispatchedAt: '2026-08-08T10:00:05Z',
    completedAt: null,
    tokensIn: 0,
    cacheReadTokens: 0,
    cacheCreationTokens: 0,
    tokensOut: 0,
    costUsd: 0.42,
    costPricingVersion: 2,
    subtreeCostUsd: 0.42,
    childCount: 0,
    expectedDurationMinutes: 10,
    nextCheckAt: null,
    checkCount: 0,
    ...overrides,
  }
}

function attentionItem(overrides: Record<string, unknown> = {}) {
  return {
    kind: 'BlockedQuestion',
    severity: 'Critical',
    taskId: 't1',
    sessionId: null,
    agentId: null,
    messageId: null,
    title: 'Which branch?',
    headline: 'Blocked — waiting on a human answer.',
    evidence: '',
    sinceUtc: '2026-08-17T09:00:00Z',
    subtreeCostUsd: null,
    actions: ['Reply'],
    ...overrides,
  }
}

function seed({
  agents = [agent({})],
  tasks = [] as unknown[],
  gitInfos = {} as Record<string, unknown>,
  worktrees = {} as Record<string, unknown>,
  attention = [] as unknown[],
} = {}) {
  server.use(
    http.get('/api/agents', () => HttpResponse.json(agents)),
    http.get('/api/agent-tasks', () => HttpResponse.json(tasks)),
    // The mobile branch's away band asks for boards; desktop never does.
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/attention', () =>
      HttpResponse.json({
        generatedAt: '2026-08-17T10:00:00Z',
        runnerConsulted: true,
        items: attention,
      }),
    ),
    // Directories not seeded read as plain non-git folders — grouping degrades gracefully.
    http.get('/api/filesystem/workspaces', ({ request }) => {
      const paths = new URL(request.url).searchParams.getAll('path')
      return HttpResponse.json(
        paths.map(
          (p) =>
            gitInfos[p] ?? {
              path: p,
              isGitRepository: false,
              repoRoot: null,
              branch: null,
              isWorktree: false,
            },
        ),
      )
    }),
    http.get('/api/filesystem/worktrees', ({ request }) => {
      const p = new URL(request.url).searchParams.get('path') ?? ''
      return HttpResponse.json(
        worktrees[p] ?? { path: p, isGitRepository: false, repoRoot: null, worktrees: [] },
      )
    }),
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

  it('a ClaudeCode rail row with no usage yet says no turns yet, never Compacted', async () => {
    seed({
      agents: [
        agent({
          id: 'a1',
          name: 'axc',
          liveSession: {
            id: 's1',
            status: 'Running',
            agentKind: 'ClaudeCode',
            contextFullness: null,
            contextFullnessState: 'NoUsageYet',
          },
        }),
      ],
    })
    renderWithProviders(<HomePage />)

    await waitFor(() => expect(screen.getByTestId('session-context-badge')).toBeInTheDocument())
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('no turns yet')
    expect(badge).toHaveAttribute('data-state', 'NoUsageYet')
    expect(badge).not.toHaveAccessibleName(/Compacted/)
    expect(screen.queryByText(/Compacted/i)).not.toBeInTheDocument()
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

  it('a worktree agent folds under its repo and the workspace switcher scopes the rail', async () => {
    seed({
      agents: [
        agent({ id: 'a1', name: 'axc' }),
        agent({ id: 'wt-agent', name: 'card-runner', workingDirectory: 'C:\\wt\\card-1' }),
      ],
      gitInfos: {
        'C:\\src\\antiphon': {
          path: 'C:\\src\\antiphon',
          isGitRepository: true,
          repoRoot: 'C:\\src\\antiphon',
          branch: 'master',
          isWorktree: false,
        },
        'C:\\wt\\card-1': {
          path: 'C:\\wt\\card-1',
          isGitRepository: true,
          repoRoot: 'C:\\src\\antiphon',
          branch: 'feat/card-1',
          isWorktree: true,
        },
      },
    })
    renderWithProviders(<HomePage />)

    // One project, not two — the worktree directory folded into the repo it belongs to.
    await waitFor(() => expect(screen.getByTestId('workspace-switcher')).toBeInTheDocument())
    expect(screen.getByTestId('project-switcher')).toHaveTextContent('antiphon')
    expect(screen.getByRole('button', { name: 'Select agent axc' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Select agent card-runner' })).not.toBeInTheDocument()

    // Switch to the worktree: rail, files pane, and branch badge follow.
    await userEvent.click(screen.getByTestId('workspace-switcher'))
    await userEvent.click(await screen.findByText('card-1'))
    expect(await screen.findByRole('button', { name: 'Select agent card-runner' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Select agent axc' })).not.toBeInTheDocument()
    expect(screen.getByTestId('files-panel')).toHaveTextContent('wt-agent')
    expect(screen.getByTestId('workspace-switcher')).toHaveTextContent('feat/card-1')
  })

  it('a git worktree nobody works in is still switchable and reads as empty', async () => {
    seed({
      agents: [agent({})],
      gitInfos: {
        'C:\\src\\antiphon': {
          path: 'C:\\src\\antiphon',
          isGitRepository: true,
          repoRoot: 'C:\\src\\antiphon',
          branch: 'master',
          isWorktree: false,
        },
      },
      worktrees: {
        'C:\\src\\antiphon': {
          path: 'C:\\src\\antiphon',
          isGitRepository: true,
          repoRoot: 'C:\\src\\antiphon',
          worktrees: [
            { path: 'C:\\src\\antiphon', branch: 'master', isMain: true, isLocked: false, isDetached: false },
            { path: 'C:\\wt\\spare', branch: 'feat/spare', isMain: false, isLocked: false, isDetached: false },
          ],
        },
      },
    })
    renderWithProviders(<HomePage />)

    await waitFor(() => expect(screen.getByTestId('workspace-switcher')).toBeInTheDocument())
    await userEvent.click(screen.getByTestId('workspace-switcher'))
    await userEvent.click(await screen.findByText('spare'))

    expect(
      await screen.findByText('No agent is scoped to this worktree yet.'),
    ).toBeInTheDocument()
  })

  it('carries a Needs attention badge to the diagnostic tab when something is stuck', async () => {
    // The landing screen is where an operator actually lives, and CARD-0002's rail is unbuilt — so
    // until it lands this badge is the only route from "I am working" to "something needs me".
    seed({
      attention: [
        attentionItem({}),
        attentionItem({ kind: 'DeadSession', severity: 'Error', taskId: 't2', title: 'Ship it' }),
      ],
    })
    renderWithProviders(<HomePage />)

    const badge = await screen.findByText('Needs attention (2)')
    expect(badge.closest('a')).toHaveAttribute('href', '/orchestrator?tab=attention')
  })

  it('shows no attention badge on a quiet fleet, and counts settled failures as quiet', async () => {
    // A permanent "0" chip is a control nobody sees after a week; the badge only means something if
    // its PRESENCE does. Failures are 24h context, not something anybody has to act on.
    seed({
      attention: [
        attentionItem({ kind: 'RecentFailure', severity: 'Warning', taskId: 't3', title: 'Died' }),
      ],
    })
    renderWithProviders(<HomePage />)

    await waitFor(() => expect(screen.getByTestId('project-switcher')).toBeInTheDocument())
    await waitFor(() => expect(screen.queryByText(/Needs attention/)).not.toBeInTheDocument())
  })

  it('below 48em the home surface is the three bands, not the desktop rail squeezed narrow', async () => {
    const original = window.matchMedia
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: query === '(max-width: 48em)',
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }))
    try {
      seed({ tasks: [task({})] })
      renderWithProviders(<HomePage />)

      expect(await screen.findByTestId('mobile-home')).toBeInTheDocument()
      expect(screen.queryByTestId('project-switcher')).not.toBeInTheDocument()
    } finally {
      window.matchMedia = original
    }
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
