import { Alert, Select, Stack, Text } from '@mantine/core'
import {
  useAgentTuiModels,
  useAgentTuiProfiles,
  type AgentTuiModelDto,
} from '../../api/agentTui'

function modelLabel(model: AgentTuiModelDto): string {
  const availability =
    model.availability === 'Verified'
      ? 'verified'
      : model.availability === 'Stale'
        ? 'stale'
        : model.source === 'Curated'
          ? 'suggestion'
          : model.availability.toLowerCase()
  return `${model.displayName} (${model.identifier}) · ${availability}`
}

export function AgentTuiSelection({
  tuiProfileId,
  modelId,
  onProfileChange,
  onModelChange,
  liveSessionSelection,
}: {
  tuiProfileId: string | null
  modelId: string | null
  onProfileChange: (profileId: string | null) => void
  onModelChange: (modelId: string | null) => void
  liveSessionSelection?: {
    tuiProfileRevisionId: string | null
    effectiveModelId: string | null
    pendingRestart: boolean
  } | null
}) {
  const { data: profiles, isLoading: profilesLoading } = useAgentTuiProfiles()
  const { data: models, isLoading: modelsLoading } = useAgentTuiModels(tuiProfileId)

  const enabledProfiles = (profiles ?? []).filter((profile) => profile.isEnabled)
  const profileOptions = enabledProfiles.map((profile) => ({
    value: profile.id,
    label: `${profile.displayName} (${profile.kind})${profile.isDefault ? ' · default' : ''}`,
  }))

  const modelOptions = [
    { value: '', label: "Use the agent's tier (no exact model)" },
    ...(models ?? []).map((model) => ({
      value: model.identifier,
      label: modelLabel(model),
    })),
  ]

  const selectedProfile = enabledProfiles.find((profile) => profile.id === tuiProfileId)
  const modelArgumentCapability = selectedProfile?.capabilities.find(
    (capability) => capability.name === 'modelArgument',
  )
  const modelArgumentUnsupported = modelArgumentCapability?.state === 'Unsupported'

  return (
    <Stack gap="sm">
      <Select
        label="Runner profile"
        description="Enabled AI Agent TUI profile for this agent."
        data={profileOptions}
        value={tuiProfileId}
        onChange={(value) => {
          onProfileChange(value)
          onModelChange(null)
        }}
        searchable
        nothingFoundMessage={profilesLoading ? 'Loading…' : 'No enabled profiles'}
        required
      />

      <Select
        label="Exact model"
        description={
          modelArgumentUnsupported
            ? modelArgumentCapability?.reason
            : "Optional. Leave empty and the agent's tier chooses the model; on a profile that passes no model argument, nothing is passed."
        }
        data={modelOptions}
        value={modelId ?? ''}
        onChange={(value) => onModelChange(value && value.length > 0 ? value : null)}
        searchable
        disabled={!tuiProfileId || modelArgumentUnsupported}
        nothingFoundMessage={modelsLoading ? 'Loading…' : 'No models'}
      />

      {selectedProfile && (
        <Text size="xs" c="dimmed">
          Auth: {selectedProfile.revisionDetails.authenticationMode === 'WrapperManaged'
            ? 'wrapper-managed'
            : 'managed secrets'}
          {' · '}
          Validation: {selectedProfile.validationSummary.status}
        </Text>
      )}

      {liveSessionSelection?.pendingRestart && (
        <Alert color="yellow" title="Restart required">
          Configured selection differs from the live session
          {liveSessionSelection.effectiveModelId
            ? ` (live model: ${liveSessionSelection.effectiveModelId})`
            : ''}
          . Changes apply on the next session start.
        </Alert>
      )}
    </Stack>
  )
}
