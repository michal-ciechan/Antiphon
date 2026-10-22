import { HttpResponse, http } from 'msw'
import { fireEvent } from '@testing-library/react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import type { AgentDetailDto, AgentSummaryDto } from '../../api/agents'
import type { ProjectDto } from '../../api/projects'
import type { ProjectReadinessDto } from '../../api/projectSetup'
import { renderWithProviders, screen, userEvent, waitFor, within } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentSettingsModal } from '../agents/AgentSettingsModal'
import { ProjectConfig } from './ProjectConfig'

// CARD-0167 S5 (plan D-12): the per-kind provider endpoint group on the project settings modal and
// the agent settings modal. Names only: the key list rows below carry a sentinel `value` a buggy
// server might leak, and nothing on screen may ever show it.

beforeAll(() => { Element.prototype.scrollIntoView = vi.fn() })

const notificationMock = vi.hoisted(() => ({ show: vi.fn() }))

vi.mock('@mantine/notifications', () => ({
  notifications: notificationMock,
}))

const SENTINEL = 'sentinel-0167-never-printed'

function runnerType(kind: string, displayName: string, providerEndpoint: Record<string, unknown>) {
  return {
    kind,
    displayName,
    description: `${displayName} runner`,
    defaultModelArgumentName: null,
    authenticationModes: [],
    curatedModels: [],
    capabilities: [],
    guidance: '',
    providerEndpoint,
  }
}

const runnerTypes = [
  runnerType('ClaudeCode', 'Claude Code', {
    supported: true,
    keyRequired: false,
    environmentNames: ['ANTHROPIC_API_KEY', 'ANTHROPIC_BASE_URL'],
    argumentTemplate: [],
  }),
  runnerType('Grok', 'Grok', {
    supported: true,
    keyRequired: false,
    environmentNames: ['C0167_DESCRIPTOR_ONLY_NAME', 'GROK_CLI_CHAT_PROXY_BASE_URL'],
    argumentTemplate: [],
  }),
  runnerType('Codex', 'Codex', {
    supported: true,
    keyRequired: true,
    environmentNames: ['OPENAI_API_KEY'],
    argumentTemplate: ['-c', 'model_providers.antiphon.base_url="<url>/v1"', '-c', 'model_provider=antiphon'],
  }),
  runnerType('OpenCode', 'OpenCode', {
    supported: false,
    keyRequired: false,
    environmentNames: [],
    argumentTemplate: [],
  }),
]

function apiKey(name: string, projectId: string | null) {
  return {
    id: `key-${name}`,
    name,
    projectId,
    projectName: projectId ? 'Antiphon' : null,
    createdAt: '2026-10-22T09:00:00Z',
    updatedAt: '2026-10-22T09:00:00Z',
    // Not part of ApiKeyDto: a leaked value the UI must never render.
    value: SENTINEL,
  }
}

const apiKeys = [
  apiKey('claude-proxy-key', null),
  apiKey('project-1-key', 'project-1'),
  apiKey('other-project-key', 'project-2'),
]

const project = {
  id: 'project-1',
  name: 'Antiphon',
  gitRepositoryUrl: 'https://example.test/repo.git',
  baseBranch: 'master',
  constitutionPath: 'AGENTS.md;CLAUDE.md;README.md',
  gitHubIntegrationEnabled: false,
  notificationsEnabled: false,
  createdAt: '2026-10-22T09:00:00Z',
  updatedAt: '2026-10-22T09:00:00Z',
  defaultLaunchEnv: {},
  providerEndpoints: {
    ClaudeCode: { baseUrl: 'http://claude-proxy:8080', apiKeyName: 'claude-proxy-key' },
  },
} as ProjectDto

const readiness: ProjectReadinessDto = { projectId: 'project-1', canDispatch: true, checks: [] }

function seedProject(put: (body: Record<string, unknown>) => void, warnings: string[] = []) {
  server.use(
    http.get('/api/projects', () => HttpResponse.json([project])),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/github/repos', () => HttpResponse.json([])),
    http.get('/api/projects/readiness', () => HttpResponse.json([readiness])),
    http.get('/api/projects/:id/api-keys', () => HttpResponse.json([])),
    http.get('/api/api-keys', () => HttpResponse.json(apiKeys)),
    http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
    http.put('/api/projects/:id', async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>
      put(body)
      return HttpResponse.json({ ...project, providerEndpoints: body.providerEndpoints, warnings })
    }),
  )
}

async function openProjectEditor() {
  renderWithProviders(<ProjectConfig />)
  await userEvent.click(await screen.findByRole('button', { name: 'Edit project' }))
}

describe('ProviderEndpointFields on the project settings modal', () => {
  it('renders stored endpoints per kind and submits the parsed map', async () => {
    const put = vi.fn()
    const warning = 'Provider endpoint Grok names API key project-1-key, which is not stored in any scope yet.'
    seedProject(put, [warning])
    await openProjectEditor()

    expect(await screen.findByRole('textbox', { name: 'Claude Code base URL' })).toHaveValue('http://claude-proxy:8080')
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'Claude Code key name' })).toHaveValue('claude-proxy-key'))
    expect(screen.getByText('Key stored; its value is never shown.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Grok base URL' })).toHaveValue('')

    fireEvent.change(screen.getByRole('textbox', { name: 'Grok base URL' }), {
      target: { value: 'http://grok-proxy:9000' },
    })
    await userEvent.click(screen.getByRole('textbox', { name: 'Grok key name' }))
    // Global keys and this project's own keys; another project's key is not offered.
    expect(await screen.findByRole('option', { name: 'claude-proxy-key' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'other-project-key' })).not.toBeInTheDocument()
    expect(document.body.textContent ?? '').not.toContain(SENTINEL)
    await userEvent.click(screen.getByRole('option', { name: 'project-1-key' }))

    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(put).toHaveBeenCalled())
    expect(put.mock.calls[0][0].providerEndpoints).toEqual({
      ClaudeCode: { baseUrl: 'http://claude-proxy:8080', apiKeyName: 'claude-proxy-key' },
      Grok: { baseUrl: 'http://grok-proxy:9000', apiKeyName: 'project-1-key' },
    })
    await waitFor(() => expect(notificationMock.show).toHaveBeenCalledWith(expect.objectContaining({
      color: 'yellow',
      message: expect.stringContaining(warning),
    })))
  })

  it('omits providerEndpoints when saving an unrelated project field', async () => {
    const put = vi.fn()
    seedProject(put)
    await openProjectEditor()

    expect(await screen.findByRole('textbox', { name: 'Claude Code base URL' })).toHaveValue('http://claude-proxy:8080')
    fireEvent.change(screen.getByRole('textbox', { name: /^Name$/ }), {
      target: { value: 'Renamed project' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0][0]).toHaveProperty('name', 'Renamed project')
    expect(put.mock.calls[0][0]).not.toHaveProperty('providerEndpoints')
  })

  it('clears every endpoint with the empty map', async () => {
    const put = vi.fn()
    seedProject(put)
    await openProjectEditor()

    expect(await screen.findByRole('textbox', { name: 'Claude Code base URL' })).toHaveValue('http://claude-proxy:8080')
    await userEvent.click(screen.getByRole('button', { name: 'Clear Claude Code endpoint' }))
    expect(screen.getByRole('textbox', { name: 'Claude Code base URL' })).toHaveValue('')

    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(put).toHaveBeenCalled())
    expect(put.mock.calls[0][0].providerEndpoints).toEqual({})
  })

  it('shows the Codex key-required hint and blocks save without a key name', async () => {
    const put = vi.fn()
    seedProject(put)
    await openProjectEditor()

    const codexUrl = await screen.findByRole('textbox', { name: 'Codex base URL' })
    const codexGroup = screen.getByRole('group', { name: 'Codex endpoint' })
    expect(within(codexGroup).getByText(/Required for Codex/)).toBeInTheDocument()
    fireEvent.change(codexUrl, { target: { value: 'http://codex-proxy:7000' } })

    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(await screen.findByText('Codex endpoint needs a key name.')).toBeInTheDocument()
    expect(put).not.toHaveBeenCalled()
  })

  it('shows the projected names from the runner-types descriptor, supported kinds only', async () => {
    seedProject(vi.fn())
    await openProjectEditor()

    const grokGroup = await screen.findByRole('group', { name: 'Grok endpoint' })
    expect(within(grokGroup).getByText('C0167_DESCRIPTOR_ONLY_NAME')).toBeInTheDocument()
    expect(within(grokGroup).getByText('GROK_CLI_CHAT_PROXY_BASE_URL')).toBeInTheDocument()
    const codexGroup = screen.getByRole('group', { name: 'Codex endpoint' })
    expect(within(codexGroup).getByText('model_provider=antiphon')).toBeInTheDocument()
    expect(within(codexGroup).getByText('OPENAI_API_KEY')).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'OpenCode endpoint' })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'OpenCode base URL' })).not.toBeInTheDocument()
  })
})

const agent: AgentSummaryDto = {
  id: 'agent-1',
  name: 'Proxy Claude',
  slug: 'proxy-claude',
  workingDirectory: '/work/app',
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
  createdAt: '2026-10-22T09:00:00Z',
  updatedAt: '2026-10-22T09:00:00Z',
  liveSession: null,
  alwaysOn: false,
  remoteControlEnabled: false,
  supervision: null,
  systemPromptAppend: null,
  modelLevel: 'High',
  working: false,
  kind: 'ClaudeCode',
}

const agentDetail = {
  ...agent,
  queue: [],
  providerEndpointMode: 'Endpoint',
  providerEndpoint: { baseUrl: 'http://agent-proxy:8080', apiKeyName: 'claude-proxy-key' },
} as AgentDetailDto

function seedAgent(patch: (body: Record<string, unknown>) => void) {
  server.use(
    // BEFORE ':id', which otherwise treats "bundles" as an agent id.
    http.get('/api/agents/bundles', () => HttpResponse.json([])),
    http.get('/api/agents/:id', () => HttpResponse.json(agentDetail)),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    http.get('/api/agent-tui/runner-types', () => HttpResponse.json(runnerTypes)),
    http.get('/api/api-keys', () => HttpResponse.json(apiKeys)),
    http.patch('/api/agents/:id', async ({ request }) => {
      patch((await request.json()) as Record<string, unknown>)
      return HttpResponse.json(agentDetail)
    }),
  )
}

describe('ProviderEndpointFields on the agent settings modal', () => {
  it('Own endpoint mode submits the edited URL and full endpoint', async () => {
    const patch = vi.fn()
    seedAgent(patch)
    renderWithProviders(<AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />)

    const url = await screen.findByRole('textbox', { name: 'Claude Code base URL' })
    expect(url).toHaveValue('http://agent-proxy:8080')
    expect(screen.getByRole('radio', { name: 'Own endpoint' })).toBeChecked()
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'Claude Code key name' })).toHaveValue('claude-proxy-key'))
    fireEvent.change(url, { target: { value: 'http://edited-agent-proxy:9000' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(patch).toHaveBeenCalledTimes(1))
    expect(patch.mock.calls[0][0]).toHaveProperty('providerEndpointMode', 'Endpoint')
    expect(patch.mock.calls[0][0].providerEndpoint).toEqual({
      baseUrl: 'http://edited-agent-proxy:9000',
      apiKeyName: 'claude-proxy-key',
    })
  })

  it('Direct mode hides the fields and submits providerEndpoint null', async () => {
    let submitted: Record<string, unknown> | null = null
    seedAgent((body) => { submitted = body })

    renderWithProviders(<AgentSettingsModal agent={agent} opened onClose={() => {}} onDeleted={() => {}} />)

    expect(await screen.findByRole('textbox', { name: 'Claude Code base URL' })).toHaveValue('http://agent-proxy:8080')
    expect(screen.getByRole('radio', { name: 'Own endpoint' })).toBeChecked()

    await userEvent.click(screen.getByRole('radio', { name: 'Direct (no endpoint)' }))
    expect(screen.queryByRole('textbox', { name: 'Claude Code base URL' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted).toHaveProperty('providerEndpointMode', 'Direct')
    expect(submitted).toHaveProperty('providerEndpoint', null)
    expect(document.body.textContent ?? '').not.toContain(SENTINEL)
  })
})
