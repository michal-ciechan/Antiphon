import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import type { AgentTuiProfileDto } from '../../api/agentTui'
import { renderWithProviders, screen } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { AgentTuiSelection } from './AgentTuiSelection'

const profile = (overrides: Partial<AgentTuiProfileDto> = {}): AgentTuiProfileDto => ({
  id: 'tui-profile-1',
  displayName: 'GKP Grok',
  kind: 'Grok',
  isEnabled: true,
  isDefault: false,
  source: 'User',
  sourceDefinitionName: null,
  revisionId: 'tui-profile-revision-1',
  revision: 1,
  revisionDetails: {
    id: 'tui-profile-revision-1',
    revision: 1,
    executable: 'pwsh.exe',
    arguments: [],
    discoveryArguments: [],
    versionArguments: [],
    workingDirectory: null,
    authenticationMode: 'WrapperManaged',
    nonSecretEnvironment: {},
    secretEnvironmentNames: [],
    modelArgumentName: null,
    guidance: '',
    createdAt: '2026-05-18T09:00:00Z',
  },
  commandPreview: {
    executable: 'pwsh.exe',
    arguments: [],
    workingDirectory: null,
  },
  secretEnvironment: [],
  models: [],
  capabilities: [],
  validationSummary: {
    status: 'Succeeded',
    profileRevisionId: 'tui-profile-revision-1',
    isCurrentRevision: true,
    runnerVersion: null,
    probedAt: '2026-05-18T09:00:00Z',
  },
  createdAt: '2026-05-18T09:00:00Z',
  updatedAt: '2026-05-18T09:00:00Z',
  ...overrides,
})

function stubProfiles(profiles: AgentTuiProfileDto[]) {
  server.use(
    http.get('/api/agent-tui/profiles', () => HttpResponse.json(profiles)),
    http.get('/api/agent-tui/profiles/:id/models', () => HttpResponse.json([])),
  )
}

describe('AgentTuiSelection CARD-0182', () => {
  it('describes the tier fill-in when the profile passes a model argument', async () => {
    stubProfiles([
      profile({
        revisionDetails: {
          ...profile().revisionDetails,
          modelArgumentName: '--model',
        },
        capabilities: [
          { name: 'modelArgument', state: 'Supported', reason: 'Grok accepts --model.' },
        ],
      }),
    ])

    renderWithProviders(
      <AgentTuiSelection
        tuiProfileId="tui-profile-1"
        modelId={null}
        onProfileChange={() => {}}
        onModelChange={() => {}}
      />,
    )

    expect(
      await screen.findByText("Optional. Leave empty and the agent's tier chooses the model; on a profile that passes no model argument, nothing is passed."),
    ).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: /exact model/i })).not.toBeDisabled()
  })

  it('disables the exact-model picker with the capability reason when the profile passes none', async () => {
    const reason = 'The active revision declares no model argument.'
    stubProfiles([
      profile({
        capabilities: [{ name: 'modelArgument', state: 'Unsupported', reason }],
      }),
    ])

    renderWithProviders(
      <AgentTuiSelection
        tuiProfileId="tui-profile-1"
        modelId={null}
        onProfileChange={() => {}}
        onModelChange={() => {}}
      />,
    )

    expect(await screen.findByText(reason)).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: /exact model/i })).toBeDisabled()
  })
})
