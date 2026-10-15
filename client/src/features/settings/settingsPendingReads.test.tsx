import { onlineManager, type QueryKey } from '@tanstack/react-query'
import { act, cleanup } from '@testing-library/react'
import { delay, http, HttpResponse } from 'msw'
import type { ReactElement } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AgentTuiProfileDto } from '../../api/agentTui'
import type { BoardSummaryDto } from '../../api/boards'
import type { GitHubStatusDto, ProjectDeletionImpactDto, ProjectDto } from '../../api/projects'
import type { LlmProviderDto, ModelRoutingDto, TemplateGroupDto, WorkflowTemplateDto } from '../../api/settings'
import { server } from '../../test/mocks/server'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { AgentTuiConfig } from './AgentTuiConfig'
import { ApiKeysSection } from './ApiKeysSection'
import { ProjectConfig } from './ProjectConfig'
import { ProjectDeleteDialog } from './ProjectDeleteDialog'
import { ProviderConfig } from './ProviderConfig'
import { StatusTab } from './StatusTab'
import { TemplateManager } from './TemplateManager'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

// CARD-1274 (CARD-1270 follow-up): React Query 5 reports an offline read with no data as pending
// and paused (isLoading false, no data, no error). Neither that nor a failed read is a value: each
// Settings surface below shows `<subject> loading.`, `<subject> unknown (read paused offline).` or
// its failure text in place of a value, an empty state or a default.

const AT = '2026-10-15T09:00:00Z'

const project: ProjectDto = {
  id: 'p1',
  name: 'Antiphon',
  gitRepositoryUrl: 'https://example.test/repo.git',
  baseBranch: 'master',
  constitutionPath: 'AGENTS.md',
  gitHubIntegrationEnabled: false,
  notificationsEnabled: false,
  createdAt: AT,
  updatedAt: AT,
}

const board: BoardSummaryDto = {
  id: 'b1',
  projectId: 'p1',
  projectName: 'Antiphon',
  name: 'Main board',
  description: '',
  trackerKind: 'Internal' as BoardSummaryDto['trackerKind'],
  maxConcurrentSessions: 1,
  cardCount: 3,
  createdAt: AT,
  updatedAt: AT,
}

const impact: ProjectDeletionImpactDto = {
  projectId: 'p1',
  projectName: 'Antiphon',
  boardCount: 2,
  cardCount: 0,
  openCardCount: 0,
  runningSessionCount: 0,
  detachedAgentCount: 0,
  workflowCount: 0,
  blockers: [],
  requiresConfirmation: true,
  canDelete: true,
}

const apiKey = { id: 'key-1', name: 'anthropic-default', projectId: null, projectName: null, createdAt: AT, updatedAt: AT }

const template: WorkflowTemplateDto = {
  id: 't1',
  name: 'Feature flow',
  description: 'Ships a feature',
  yamlDefinition: 'stages: []',
  isBuiltIn: false,
  templateGroupId: null,
  templateGroupName: null,
  selectableStages: false,
  createdAt: AT,
  updatedAt: AT,
}

const templateGroup: TemplateGroupDto = {
  id: 'g1', name: 'Release group', description: '', isBuiltIn: false, templateCount: 0, createdAt: AT, updatedAt: AT,
}

const provider: LlmProviderDto = {
  id: 'pr1',
  name: 'Anthropic main',
  providerType: 0,
  apiKeyMasked: 'sk-***',
  baseUrl: 'https://api.example.test',
  isEnabled: true,
  defaultModel: 'claude-default',
  createdAt: AT,
  updatedAt: AT,
}

const routing: ModelRoutingDto = {
  id: 'r1', stageName: 'implement', modelName: 'claude-routed', providerId: 'pr1', workflowTemplateId: 't1', createdAt: AT,
}

const githubStatus: GitHubStatusDto = {
  enabled: true,
  baseUrl: 'https://api.github.example',
  connected: true,
  authenticatedAs: 'octo',
  error: null,
  repoCache: { count: 7, lastRefreshed: null, isStale: false, ttlMinutes: 30 },
}

const profile: AgentTuiProfileDto = {
  id: 'tui-claude',
  displayName: 'Claude profile',
  kind: 'ClaudeCode',
  isEnabled: true,
  isDefault: true,
  source: 'User',
  sourceDefinitionName: null,
  revisionId: 'rev-claude',
  revision: 1,
  revisionDetails: {
    id: 'rev-claude',
    revision: 1,
    executable: 'claude',
    arguments: [],
    discoveryArguments: [],
    versionArguments: [],
    workingDirectory: null,
    authenticationMode: 'WrapperManaged',
    nonSecretEnvironment: {},
    secretEnvironmentNames: [],
    modelArgumentName: '--model',
    guidance: '',
    createdAt: AT,
  },
  commandPreview: { executable: 'claude', arguments: [], workingDirectory: null },
  secretEnvironment: [],
  models: [],
  capabilities: [],
  validationSummary: {
    status: 'Succeeded',
    profileRevisionId: 'rev-claude',
    isCurrentRevision: true,
    runnerVersion: null,
    probedAt: AT,
  },
  createdAt: AT,
  updatedAt: AT,
} as AgentTuiProfileDto

/** Every read the rendered surfaces make, populated; a row's own read is overridden per state. */
function servePopulated() {
  server.use(
    http.get('/api/projects/p1/deletion-impact', () => HttpResponse.json(impact)),
    http.get('/api/api-keys/global', () => HttpResponse.json([apiKey])),
    http.get('/api/settings/templates', () => HttpResponse.json([template])),
    http.get('/api/settings/template-groups', () => HttpResponse.json([templateGroup])),
    http.get('/api/settings/providers', () => HttpResponse.json([provider])),
    http.get('/api/settings/templates/t1/model-routing', () => HttpResponse.json([routing])),
    http.get('/api/projects', () => HttpResponse.json([project])),
    http.get('/api/boards', () => HttpResponse.json([board])),
    http.get('/api/github/repos', () => HttpResponse.json([])),
    http.get('/api/projects/readiness', () => HttpResponse.json([])),
    http.get('/api/github/status', () => HttpResponse.json(githubStatus)),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([profile])),
  )
}

type C1274State = 'loading' | 'paused' | 'failed' | 'success'
const c1274States: C1274State[] = ['loading', 'paused', 'failed', 'success']

interface C1274Read {
  render: () => ReactElement
  url: string
  key: QueryKey
  /** The element that holds the read's value, empty state or pending text. */
  surface: () => HTMLElement
  loading: string
  paused: string
  failed: string
  /** Values and controls a successful read shows, absent in every other state. */
  shown: string[]
  /** Empty states and defaults this read's populated fixture never shows. */
  definite: string[]
  /** Buttons that write and stay disabled until the read has loaded. */
  blocked?: { name: string; prepare?: () => Promise<void> }[]
}

const page = () => document.body

async function typeNewKey() {
  await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'anthropic-default')
  await userEvent.type(screen.getByLabelText('Value (missing)'), 'new-secret-value')
}

const c1274Reads: Record<string, C1274Read> = {
  'project deletion impact': {
    render: () => <ProjectDeleteDialog project={project} onClose={() => undefined} />,
    url: '/api/projects/p1/deletion-impact',
    key: ['projects', 'p1', 'deletion-impact'],
    surface: () => screen.getByRole('dialog'),
    loading: 'Deletion impact loading.',
    paused: 'Deletion impact unknown (read paused offline).',
    failed: 'Could not check what this project owns. Delete stays disabled until the check loads.',
    shown: ['2 boards', 'I understand this deletes everything listed above'],
    definite: ['This project has no boards or cards.'],
    blocked: [{ name: 'Delete' }],
  },
  'api keys': {
    render: () => <ApiKeysSection />,
    url: '/api/api-keys/global',
    key: ['api-keys', 'global'],
    surface: page,
    loading: 'API keys loading.',
    paused: 'API keys unknown (read paused offline).',
    failed: 'boom',
    shown: ['anthropic-default (configured)'],
    definite: ['No global API keys configured.'],
    blocked: [{ name: 'Save key', prepare: typeNewKey }],
  },
  templates: {
    render: () => <TemplateManager />,
    url: '/api/settings/templates',
    key: ['settings', 'templates'],
    surface: page,
    loading: 'Templates loading.',
    paused: 'Templates unknown (read paused offline).',
    failed: 'Error loading templates',
    shown: ['Feature flow'],
    definite: ['No templates found.', 'No template groups.'],
  },
  'template groups': {
    render: () => <TemplateManager />,
    url: '/api/settings/template-groups',
    key: ['template-groups'],
    surface: () => screen.getByTestId('template-groups-section'),
    loading: 'Template groups loading.',
    paused: 'Template groups unknown (read paused offline).',
    failed: 'Could not load template groups.',
    shown: ['Release group'],
    definite: ['No template groups.'],
  },
  'template providers': {
    render: () => <TemplateManager />,
    url: '/api/settings/providers',
    key: ['settings', 'providers'],
    surface: () => screen.getByTestId('template-model-routing-t1'),
    loading: 'Providers loading.',
    paused: 'Providers unknown (read paused offline).',
    failed: 'Could not load providers.',
    shown: ['Anthropic main'],
    definite: ['Add a provider first to configure model routing.', 'No routing rules.'],
  },
  'template model routing': {
    render: () => <TemplateManager />,
    url: '/api/settings/templates/t1/model-routing',
    key: ['settings', 'model-routing', 't1'],
    surface: () => screen.getByTestId('template-model-routing-t1'),
    loading: 'Model routing loading.',
    paused: 'Model routing unknown (read paused offline).',
    failed: 'Could not load model routing.',
    shown: ['claude-routed'],
    definite: ['No routing rules.', 'Add a provider first to configure model routing.'],
  },
  projects: {
    render: () => <ProjectConfig />,
    url: '/api/projects',
    key: ['projects'],
    surface: page,
    loading: 'Projects loading.',
    paused: 'Projects unknown (read paused offline).',
    failed: 'Error loading projects',
    shown: ['https://example.test/repo.git'],
    definite: ['No projects yet.'],
  },
  'project boards': {
    render: () => <ProjectConfig />,
    url: '/api/boards',
    key: ['boards'],
    surface: () => screen.getByTestId('project-boards-p1'),
    loading: 'Boards loading.',
    paused: 'Boards unknown (read paused offline).',
    failed: 'Could not load boards.',
    shown: ['Main board'],
    definite: ['--'],
  },
  'agent tui profiles': {
    render: () => <AgentTuiConfig />,
    url: '/api/agent-tui/profiles',
    key: ['agent-tui', 'profiles'],
    surface: page,
    loading: 'AI Agent TUI profiles loading.',
    paused: 'AI Agent TUI profiles unknown (read paused offline).',
    failed: 'Error loading AI Agent TUI profiles',
    // The empty table and New profile read as a definite empty list.
    shown: ['Claude profile', 'Validation', 'New profile'],
    definite: [],
  },
  'github status': {
    render: () => <StatusTab />,
    url: '/api/github/status',
    key: ['github', 'status'],
    surface: page,
    loading: 'GitHub status loading.',
    paused: 'GitHub status unknown (read paused offline).',
    failed: 'Error loading status',
    shown: ['Connected as octo', '30 minutes'],
    definite: ['Not connected', '15 minutes'],
  },
  providers: {
    render: () => <ProviderConfig />,
    url: '/api/settings/providers',
    key: ['settings', 'providers'],
    surface: page,
    loading: 'Providers loading.',
    paused: 'Providers unknown (read paused offline).',
    failed: 'Error loading provider configuration',
    shown: ['Anthropic main'],
    definite: ['No providers configured.'],
  },
}

function c1274Present(surface: HTMLElement, text: string): boolean {
  return (surface.textContent ?? '').includes(text)
}

describe('C1274 Settings read state table', () => {
  afterEach(() => {
    cleanup()
    onlineManager.setOnline(true)
  })

  it.each(Object.keys(c1274Reads).flatMap((read) => c1274States.map((state) => ({ read, state }))))(
    'C1274 $read read $state',
    async ({ read: name, state }: { read: string; state: C1274State }) => {
      const read = c1274Reads[name]
      servePopulated()
      // msw checks the most recently added handler first, so the read under test wins.
      if (state === 'loading') {
        server.use(http.get(read.url, async () => { await delay('infinite'); return HttpResponse.json([]) }))
      } else if (state === 'failed') {
        server.use(http.get(read.url, () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
      }
      const { queryClient } = renderWithProviders(read.render())

      if (state === 'paused') {
        await waitFor(() => expect(c1274Present(read.surface(), read.shown[0])).toBe(true))
        act(() => {
          onlineManager.setOnline(false)
          void queryClient.resetQueries({ queryKey: read.key, exact: true })
        })
      }

      // The read reached the named state.
      await waitFor(() => {
        const query = queryClient.getQueryState(read.key)
        expect(query).toBeDefined()
        expect({ status: query?.status, fetchStatus: query?.fetchStatus, hasData: query?.data !== undefined }).toEqual({
          loading: { status: 'pending', fetchStatus: 'fetching', hasData: false },
          paused: { status: 'pending', fetchStatus: 'paused', hasData: false },
          failed: { status: 'error', fetchStatus: 'idle', hasData: false },
          success: { status: 'success', fetchStatus: 'idle', hasData: true },
        }[state])
      })

      const expected: string | null = state === 'success' ? null : read[state]
      await waitFor(() => {
        for (const label of [read.loading, read.paused, read.failed]) {
          expect({ label, present: c1274Present(read.surface(), label) }).toEqual({ label, present: label === expected })
        }
        for (const text of read.shown) {
          expect({ text, present: c1274Present(read.surface(), text) }).toEqual({ text, present: state === 'success' })
        }
      })
      for (const text of read.definite) {
        expect({ text, present: c1274Present(read.surface(), text) }).toEqual({ text, present: false })
      }
      for (const button of read.blocked ?? []) {
        // A button that is not rendered cannot write either.
        if (state === 'success' || screen.queryByRole('button', { name: button.name })) {
          await button.prepare?.()
          const control = screen.getByRole('button', { name: button.name })
          if (state === 'success') {
            if (name !== 'project deletion impact') expect(control).toBeEnabled()
          } else {
            expect(control).toBeDisabled()
          }
        }
      }
    },
  )
})

describe('C1274 offline from the first render', () => {
  afterEach(() => {
    cleanup()
    onlineManager.setOnline(true)
  })

  it('C1274 project delete dialog offline sends no DELETE and shows no impact until the check loads', async () => {
    const write = vi.fn()
    servePopulated()
    server.use(http.delete('/api/projects/p1', ({ request }) => { write(`DELETE ${new URL(request.url).search}`); return new HttpResponse(null, { status: 204 }) }))
    onlineManager.setOnline(false)
    renderWithProviders(<ProjectDeleteDialog project={project} onClose={() => undefined} />)

    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Deletion impact unknown (read paused offline).')).toBeInTheDocument()
    expect(dialog).not.toHaveTextContent('This project has no boards or cards.')
    expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeDisabled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    act(() => { onlineManager.setOnline(true) })
    expect(await within(dialog).findByText('2 boards')).toBeInTheDocument()
    expect(within(dialog).queryByText('Deletion impact unknown (read paused offline).')).not.toBeInTheDocument()
    expect(write).not.toHaveBeenCalled()
  })

  it('C1274 api keys offline keep Save key disabled with a typed key and send no PUT after reconnect', async () => {
    const write = vi.fn()
    servePopulated()
    server.use(http.put('/api/api-keys/:name', ({ params }) => { write(`PUT ${String(params.name)}`); return HttpResponse.json(apiKey) }))
    onlineManager.setOnline(false)
    renderWithProviders(<ApiKeysSection />)

    expect(await screen.findByText('API keys unknown (read paused offline).')).toBeInTheDocument()
    expect(screen.queryByText('No global API keys configured.')).not.toBeInTheDocument()
    await typeNewKey()
    expect(screen.getByRole('button', { name: 'Save key' })).toBeDisabled()
    expect(screen.getByText('Save key is disabled until the API key list loads: a key with the same name would be replaced.')).toBeInTheDocument()

    act(() => { onlineManager.setOnline(true) })
    expect(await screen.findByText('anthropic-default (configured)')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save key' })).toBeEnabled())
    expect(screen.queryByText(/Save key is disabled until/)).not.toBeInTheDocument()
    expect(write).not.toHaveBeenCalled()
  })

  it.each([
    { name: 'ProjectConfig', ui: () => <ProjectConfig />, text: 'Projects unknown (read paused offline).', definite: 'No projects yet.' },
    { name: 'ProviderConfig', ui: () => <ProviderConfig />, text: 'Providers unknown (read paused offline).', definite: 'No providers configured.' },
    { name: 'TemplateManager', ui: () => <TemplateManager />, text: 'Templates unknown (read paused offline).', definite: 'No templates found.' },
    { name: 'AgentTuiConfig', ui: () => <AgentTuiConfig />, text: 'AI Agent TUI profiles unknown (read paused offline).', definite: 'New profile' },
    { name: 'StatusTab', ui: () => <StatusTab />, text: 'GitHub status unknown (read paused offline).', definite: 'Not connected' },
  ])('C1274 $name offline from the first render shows its read as unknown', async ({ ui, text, definite }) => {
    servePopulated()
    onlineManager.setOnline(false)
    renderWithProviders(ui())
    expect(await screen.findByText(text)).toBeInTheDocument()
    expect(document.body).not.toHaveTextContent(definite)
  })
})

describe('C1274 project delete dialog after a failed refetch', () => {
  it('C1274 project delete dialog hides the stale impact and keeps Delete disabled after a failed refetch', async () => {
    const write = vi.fn()
    servePopulated()
    server.use(http.delete('/api/projects/p1', () => { write('DELETE'); return new HttpResponse(null, { status: 204 }) }))
    const { queryClient } = renderWithProviders(<ProjectDeleteDialog project={project} onClose={() => undefined} />)
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('2 boards')).toBeInTheDocument()

    server.use(http.get('/api/projects/p1/deletion-impact', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
    await act(async () => { await queryClient.refetchQueries({ queryKey: ['projects', 'p1', 'deletion-impact'], exact: true }) })
    await waitFor(() => expect(queryClient.getQueryState(['projects', 'p1', 'deletion-impact'])).toMatchObject({ status: 'error', fetchStatus: 'idle' }))
    expect(queryClient.getQueryData(['projects', 'p1', 'deletion-impact'])).toEqual(impact)

    expect(await within(dialog).findByText('Could not check what this project owns. Delete stays disabled until the check loads.')).toBeInTheDocument()
    expect(within(dialog).queryByText('2 boards')).not.toBeInTheDocument()
    expect(within(dialog).queryByText('I understand this deletes everything listed above')).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeDisabled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))
    expect(write).not.toHaveBeenCalled()
  })
})
