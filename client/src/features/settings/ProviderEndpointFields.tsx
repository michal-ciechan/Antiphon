import { Button, Code, Fieldset, Group, Radio, Select, Stack, Text, TextInput } from '@mantine/core'
import { useMemo } from 'react'
import type { ProviderEndpointMode } from '../../api/agents'
import { useAllApiKeys } from '../../api/apiKeys'
import type { AgentKind, AgentTuiRunnerTypeDto } from '../../api/agentTui'
import {
  EMPTY_PROVIDER_ENDPOINT_DRAFT,
  providerEndpointKeyNames,
  useProviderEndpointKinds,
  type ProviderEndpointDraft,
  type ProviderEndpointDrafts,
} from './providerEndpointModel'

// CARD-0167 S5 (plan D-12): the "LLM endpoint" group. A provider endpoint is a base URL and a key
// NAME from the API-key store. Key values are write-only on the server and never reach this code:
// the picker lists names from GET /api/api-keys and shows only whether a key of that name is stored.

interface ProviderEndpointFieldsProps {
  runnerType: AgentTuiRunnerTypeDto
  value: ProviderEndpointDraft
  onChange: (value: ProviderEndpointDraft) => void
  keyNames: string[]
  onClear?: () => void
  error?: string | null
}

/** One kind's base URL, key-name picker and the names the setting projects (read-only). */
export function ProviderEndpointFields({ runnerType, value, onChange, keyNames, onClear, error }: ProviderEndpointFieldsProps) {
  const name = runnerType.displayName
  const descriptor = runnerType.providerEndpoint
  const keyRequired = descriptor?.keyRequired ?? false
  // A stored name that is not (yet) in the key store stays selectable: keys may be created later (D-8).
  const options = value.apiKeyName && !keyNames.includes(value.apiKeyName) ? [...keyNames, value.apiKeyName] : keyNames
  const stored = !!value.apiKeyName && keyNames.includes(value.apiKeyName)

  return (
    <Fieldset legend={`${name} endpoint`} variant="filled">
      <Stack gap="xs">
        <TextInput
          label={`${name} base URL`}
          placeholder="https://llm-proxy.example:8443"
          value={value.baseUrl}
          onChange={(event) => onChange({ ...value, baseUrl: event.currentTarget.value })}
        />
        <Select
          label={`${name} key name`}
          description={
            keyRequired
              ? `Required for ${name}: the key the proxy expects, by name from the API keys.`
              : 'Optional: a key-injecting proxy needs only the URL.'
          }
          placeholder="No key"
          data={options}
          value={value.apiKeyName}
          onChange={(apiKeyName) => onChange({ ...value, apiKeyName })}
          searchable
          clearable
        />
        {value.apiKeyName && (
          <Text size="xs" c={stored ? 'dimmed' : 'orange'}>
            {stored
              ? 'Key stored; its value is never shown.'
              : `No stored key named ${value.apiKeyName} yet; launches fail until it is stored.`}
          </Text>
        )}
        {descriptor && descriptor.environmentNames.length > 0 && (
          <Group gap={4}>
            <Text size="xs" c="dimmed">Sets</Text>
            {descriptor.environmentNames.map((variable) => <Code key={variable}>{variable}</Code>)}
          </Group>
        )}
        {descriptor && descriptor.argumentTemplate.length > 0 && (
          <Group gap={4}>
            <Text size="xs" c="dimmed">Appends</Text>
            {descriptor.argumentTemplate.map((argument, index) => <Code key={index}>{argument}</Code>)}
          </Group>
        )}
        {error && <Text size="sm" c="red">{error}</Text>}
        {onClear && (
          <Group justify="flex-end">
            <Button variant="subtle" size="xs" aria-label={`Clear ${name} endpoint`} onClick={onClear}>
              Clear
            </Button>
          </Group>
        )}
      </Stack>
    </Fieldset>
  )
}

interface ProjectProviderEndpointsProps {
  projectId: string
  drafts: ProviderEndpointDrafts
  onChange: (drafts: ProviderEndpointDrafts) => void
  errors: Record<string, string>
}

/** The project settings variant: one group per supported kind. */
export function ProjectProviderEndpoints({ projectId, drafts, onChange, errors }: ProjectProviderEndpointsProps) {
  const kinds = useProviderEndpointKinds()
  const keys = useAllApiKeys(kinds.length > 0)
  const keyNames = useMemo(() => providerEndpointKeyNames(keys.data, projectId), [keys.data, projectId])
  if (kinds.length === 0) return null

  return (
    <Stack gap="sm">
      <Text size="xs" c="dimmed">
        The trusted LLM proxy this project&apos;s agents of each kind use. An agent can override or opt out.
      </Text>
      {kinds.map((runnerType) => (
        <ProviderEndpointFields
          key={runnerType.kind}
          runnerType={runnerType}
          value={drafts[runnerType.kind] ?? EMPTY_PROVIDER_ENDPOINT_DRAFT}
          onChange={(value) => onChange({ ...drafts, [runnerType.kind]: value })}
          onClear={() => onChange({ ...drafts, [runnerType.kind]: EMPTY_PROVIDER_ENDPOINT_DRAFT })}
          keyNames={keyNames}
          error={errors[runnerType.kind]}
        />
      ))}
    </Stack>
  )
}

interface AgentProviderEndpointProps {
  kind: AgentKind
  projectId: string | null
  mode: ProviderEndpointMode
  onModeChange: (mode: ProviderEndpointMode) => void
  draft: ProviderEndpointDraft
  onChange: (draft: ProviderEndpointDraft) => void
  error?: string | null
}

/** The agent settings variant: the mode radio, and the fields only in Endpoint mode. */
export function AgentProviderEndpoint({ kind, projectId, mode, onModeChange, draft, onChange, error }: AgentProviderEndpointProps) {
  const kinds = useProviderEndpointKinds()
  const runnerType = kinds.find((type) => type.kind === kind)
  const keys = useAllApiKeys(!!runnerType)
  const keyNames = useMemo(() => providerEndpointKeyNames(keys.data, projectId), [keys.data, projectId])
  if (!runnerType) return null

  return (
    <Stack gap="xs">
      <Radio.Group
        label="LLM endpoint"
        description="Changes apply on the next launch."
        value={mode}
        onChange={(value) => onModeChange(value as ProviderEndpointMode)}
      >
        <Group mt={4}>
          <Radio value="Inherit" label="Inherit project endpoint" />
          <Radio value="Endpoint" label="Own endpoint" />
          <Radio value="Direct" label="Direct (no endpoint)" />
        </Group>
      </Radio.Group>
      {mode === 'Endpoint' && (
        <ProviderEndpointFields runnerType={runnerType} value={draft} onChange={onChange} keyNames={keyNames} error={error} />
      )}
    </Stack>
  )
}
