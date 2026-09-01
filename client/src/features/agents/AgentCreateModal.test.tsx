import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { CreateAgentRequest } from '../../api/agents'
import type { AgentTuiProfileDto } from '../../api/agentTui'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentCreateModal } from './AgentCreateModal'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

const claudeProfile: AgentTuiProfileDto = {
  id: 'tui-claude',
  displayName: 'Claude',
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
    createdAt: '2026-05-18T09:00:00Z',
  },
  commandPreview: { executable: 'claude', arguments: [], workingDirectory: null },
  secretEnvironment: [],
  models: [],
  capabilities: [{
    name: 'remoteControl',
    state: 'Supported',
    reason: "Claude supports Antiphon's remote-control launch behaviour.",
  }],
  validationSummary: {
    status: 'Succeeded',
    profileRevisionId: 'rev-claude',
    isCurrentRevision: true,
    runnerVersion: null,
    probedAt: '2026-05-18T09:00:00Z',
  },
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
}

function browseHandler() {
  return http.get('/api/filesystem/browse', ({ request }) => {
    const path = new URL(request.url).searchParams.get('path') ?? ''
    return HttpResponse.json({
      normalizedPath: path,
      exists: path.length > 0,
      isDrivesListing: path.length === 0,
      suggestions: [],
    })
  })
}

function seedCreate(onPost: (request: CreateAgentRequest) => void) {
  server.use(
    browseHandler(),
    http.get('/api/agent-tui/profiles', () => HttpResponse.json([claudeProfile])),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
    http.get('/api/boards', () => HttpResponse.json([])),
    http.get('/api/agents/bundles', () => HttpResponse.json([
      { key: 'orchestrator', version: '1', stamp: 'orchestrator v1', summary: 'You are an orchestrator.', chars: 10 },
      { key: 'board-api', version: '1', stamp: 'board-api v1', summary: 'Working the Antiphon board.', chars: 10 },
    ])),
    http.post('/api/agents', async ({ request }) => {
      const body = (await request.json()) as CreateAgentRequest
      onPost(body)
      return HttpResponse.json({ id: 'new-agent', ...body, status: 'Idle', queue: [] })
    }),
  )
}

async function fillRequiredFields() {
  const name = screen.getAllByLabelText('Name').find((el) => el instanceof HTMLInputElement) as HTMLInputElement
  await userEvent.type(name, 'CARD-0255 throwaway')
  const directory = screen
    .getAllByLabelText('Working directory')
    .find((el): el is HTMLInputElement => el instanceof HTMLInputElement)!
  await userEvent.clear(directory)
  await userEvent.type(directory, 'D:\\src\\card-0255')
}

describe('AgentCreateModal presets', () => {
  it('defaults to Standing orchestrator and submits preset with filled bundles', async () => {
    let submitted: CreateAgentRequest | null = null
    seedCreate((request) => { submitted = request })

    renderWithProviders(<AgentCreateModal opened onClose={() => {}} />)

    expect(await screen.findByText('Standing orchestrator')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('switch', { name: /Always on/i })).toBeChecked())

    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted).toMatchObject({
      preset: 'orchestrator',
      alwaysOn: true,
      bundleKeys: ['orchestrator', 'board-api'],
      remoteControlEnabled: true,
    })
  })

  it('Worker chip submits the worker preset and empty bundles', async () => {
    let submitted: CreateAgentRequest | null = null
    seedCreate((request) => { submitted = request })

    renderWithProviders(<AgentCreateModal opened onClose={() => {}} />)

    expect(await screen.findByText('Standing orchestrator')).toBeInTheDocument()
    await userEvent.click(screen.getByText('Worker'))
    await waitFor(() => expect(screen.getByRole('switch', { name: /Always on/i })).not.toBeChecked())

    await fillRequiredFields()
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(submitted).not.toBeNull())
    expect(submitted).toMatchObject({
      preset: 'worker',
      alwaysOn: false,
      bundleKeys: [],
      remoteControlEnabled: false,
    })
  })
})
