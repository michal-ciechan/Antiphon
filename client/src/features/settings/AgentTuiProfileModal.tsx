import { useMemo, useState } from 'react'
import {
  Alert,
  Badge,
  Button,
  Group,
  Modal,
  PasswordInput,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { TbAlertCircle } from 'react-icons/tb'
import {
  useAgentTuiRunnerTypes,
  useClearAgentTuiSecret,
  useCreateAgentTuiProfile,
  usePutAgentTuiSecret,
  useUpdateAgentTuiProfile,
  type AgentKind,
  type AgentTuiAuthenticationMode,
  type AgentTuiProfileDto,
  type AgentTuiProfileWriteRequest,
} from '../../api/agentTui'
import { getApiErrorMessage } from '../../api/client'
import { envToText, textToEnv } from '../../shared/environmentText'

function splitLines(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
}

export function AgentTuiProfileModal({
  opened,
  onClose,
  profile,
}: {
  opened: boolean
  onClose: () => void
  profile: AgentTuiProfileDto | null
}) {
  const { data: runnerTypes } = useAgentTuiRunnerTypes()
  const createMutation = useCreateAgentTuiProfile()
  const updateMutation = useUpdateAgentTuiProfile(profile?.id ?? '')
  const putSecret = usePutAgentTuiSecret(profile?.id ?? '')
  const clearSecret = useClearAgentTuiSecret(profile?.id ?? '')

  const [displayName, setDisplayName] = useState('')
  const [kind, setKind] = useState<AgentKind>('OpenCode')
  const [isEnabled, setIsEnabled] = useState(true)
  const [isDefault, setIsDefault] = useState(false)
  const [executable, setExecutable] = useState('')
  const [argumentsText, setArgumentsText] = useState('')
  const [discoveryText, setDiscoveryText] = useState('')
  const [versionText, setVersionText] = useState('')
  const [workingDirectory, setWorkingDirectory] = useState('')
  const [authenticationMode, setAuthenticationMode] =
    useState<AgentTuiAuthenticationMode>('WrapperManaged')
  const [envText, setEnvText] = useState('')
  const [secretNamesText, setSecretNamesText] = useState('')
  const [modelArgumentName, setModelArgumentName] = useState('--model')
  const [guidance, setGuidance] = useState('')
  const [secretValues, setSecretValues] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)

  // (Re)populate the form when the modal opens or is pointed at a different profile — adjusted
  // during render rather than in an effect, so the previous profile's values never flash.
  const [prevKey, setPrevKey] = useState<{ opened: boolean; profile: AgentTuiProfileDto | null }>({
    opened,
    profile,
  })
  if (opened !== prevKey.opened || profile !== prevKey.profile) {
    setPrevKey({ opened, profile })
    if (opened) {
      populateForm()
    }
  }

  function populateForm() {
    if (profile) {
      setDisplayName(profile.displayName)
      setKind(profile.kind)
      setIsEnabled(profile.isEnabled)
      setIsDefault(profile.isDefault)
      setExecutable(profile.revisionDetails.executable)
      setArgumentsText(profile.revisionDetails.arguments.join('\n'))
      setDiscoveryText(profile.revisionDetails.discoveryArguments.join('\n'))
      setVersionText(profile.revisionDetails.versionArguments.join('\n'))
      setWorkingDirectory(profile.revisionDetails.workingDirectory ?? '')
      setAuthenticationMode(profile.revisionDetails.authenticationMode)
      setEnvText(envToText(profile.revisionDetails.nonSecretEnvironment))
      setSecretNamesText(profile.revisionDetails.secretEnvironmentNames.join('\n'))
      setModelArgumentName(profile.revisionDetails.modelArgumentName ?? '')
      setGuidance(profile.revisionDetails.guidance)
      setSecretValues({})
    } else {
      setDisplayName('')
      setKind('OpenCode')
      setIsEnabled(true)
      setIsDefault(false)
      setExecutable('pwsh.exe')
      setArgumentsText('-NoProfile\n-ExecutionPolicy\nBypass\n-File\nocg.ps1\n--auto\n--mini')
      setDiscoveryText('-NoProfile\n-ExecutionPolicy\nBypass\n-File\nocg.ps1\nmodels')
      setVersionText('-NoProfile\n-ExecutionPolicy\nBypass\n-File\nocg.ps1\n--version')
      setWorkingDirectory('')
      setAuthenticationMode('WrapperManaged')
      setEnvText('')
      setSecretNamesText('')
      setModelArgumentName('--model')
      setGuidance('')
      setSecretValues({})
    }
    setError(null)
  }

  const selectedRunner = useMemo(
    () => runnerTypes?.find((runner) => runner.kind === kind),
    [runnerTypes, kind],
  )

  const commandPreview = useMemo(() => {
    const args = splitLines(argumentsText)
    return `${executable} ${args.join(' ')}`.trim()
  }, [executable, argumentsText])

  const buildRequest = (): AgentTuiProfileWriteRequest => ({
    displayName: displayName.trim(),
    kind,
    isEnabled,
    isDefault,
    executable: executable.trim(),
    arguments: splitLines(argumentsText),
    discoveryArguments: splitLines(discoveryText),
    versionArguments: splitLines(versionText),
    workingDirectory: workingDirectory.trim() || null,
    authenticationMode,
    nonSecretEnvironment: textToEnv(envText),
    secretEnvironmentNames: splitLines(secretNamesText),
    modelArgumentName: modelArgumentName.trim() || null,
    guidance,
    models: [],
    expectedRevision: profile?.revision ?? null,
  })

  const save = async () => {
    setError(null)
    try {
      const body = buildRequest()
      if (profile) {
        await updateMutation.mutateAsync(body)
        for (const [name, value] of Object.entries(secretValues)) {
          if (!value) continue
          await putSecret.mutateAsync({
            environmentName: name,
            value,
            expectedRevision: profile.revision,
          })
          setSecretValues((current) => ({ ...current, [name]: '' }))
        }
      } else {
        await createMutation.mutateAsync(body)
      }
      onClose()
    } catch (err) {
      setError(getApiErrorMessage(err, 'Save failed'))
    }
  }

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={profile ? `Edit ${profile.displayName}` : 'New AI Agent TUI profile'}
      size="xl"
    >
      <Stack gap="sm">
        {error && (
          <Alert color="red" icon={<TbAlertCircle />}>
            {error}
          </Alert>
        )}

        <TextInput
          label="Display name"
          value={displayName}
          onChange={(event) => setDisplayName(event.currentTarget.value)}
          required
        />
        <Select
          label="Runner type"
          data={(runnerTypes ?? []).map((runner) => ({
            value: runner.kind,
            label: runner.displayName,
          }))}
          value={kind}
          onChange={(value) => value && setKind(value as AgentKind)}
        />
        <Group>
          <Switch
            label="Enabled"
            checked={isEnabled}
            onChange={(event) => setIsEnabled(event.currentTarget.checked)}
          />
          <Switch
            label="Installation default"
            checked={isDefault}
            onChange={(event) => setIsDefault(event.currentTarget.checked)}
          />
        </Group>

        <TextInput
          label="Executable"
          value={executable}
          onChange={(event) => setExecutable(event.currentTarget.value)}
          required
        />
        <Textarea
          label="Launch arguments (one per line)"
          description="Ordered arguments — not a shell string."
          minRows={3}
          value={argumentsText}
          onChange={(event) => setArgumentsText(event.currentTarget.value)}
        />
        <Textarea
          label="Discovery arguments (one per line)"
          minRows={2}
          value={discoveryText}
          onChange={(event) => setDiscoveryText(event.currentTarget.value)}
        />
        <Textarea
          label="Version arguments (one per line)"
          minRows={2}
          value={versionText}
          onChange={(event) => setVersionText(event.currentTarget.value)}
        />
        <TextInput
          label="Working directory"
          value={workingDirectory}
          onChange={(event) => setWorkingDirectory(event.currentTarget.value)}
        />
        <Select
          label="Authentication mode"
          data={[
            { value: 'WrapperManaged', label: 'Wrapper-managed (no secrets stored)' },
            { value: 'ManagedEnvironment', label: 'Antiphon-managed secrets' },
          ]}
          value={authenticationMode}
          onChange={(value) =>
            value && setAuthenticationMode(value as AgentTuiAuthenticationMode)
          }
        />
        <Textarea
          label="Non-secret environment (KEY=value per line)"
          minRows={2}
          value={envText}
          onChange={(event) => setEnvText(event.currentTarget.value)}
        />
        <Textarea
          label="Secret environment names (one per line)"
          description="Write-only after save. Values never reappear."
          minRows={2}
          value={secretNamesText}
          onChange={(event) => setSecretNamesText(event.currentTarget.value)}
        />

        {profile && authenticationMode === 'ManagedEnvironment' && (
          <Stack gap="xs">
            <Text size="sm" fw={500}>
              Managed secrets
            </Text>
            {profile.secretEnvironment.map((secret) => (
              <Group key={secret.name} align="flex-end" grow>
                <PasswordInput
                  label={`${secret.name} ${secret.configured ? '(configured)' : '(missing)'}`}
                  aria-label={`Secret value for ${secret.name}`}
                  value={secretValues[secret.name] ?? ''}
                  onChange={(event) =>
                    setSecretValues((current) => ({
                      ...current,
                      [secret.name]: event.currentTarget.value,
                    }))
                  }
                  placeholder={secret.configured ? 'Enter new value to replace' : 'Enter value'}
                />
                <Button
                  variant="light"
                  onClick={async () => {
                    const value = secretValues[secret.name]
                    if (!value) return
                    try {
                      await putSecret.mutateAsync({
                        environmentName: secret.name,
                        value,
                        expectedRevision: profile.revision,
                      })
                      setSecretValues((current) => ({ ...current, [secret.name]: '' }))
                    } catch (err) {
                      setError(getApiErrorMessage(err, 'Secret save failed'))
                    }
                  }}
                >
                  Save secret
                </Button>
                {secret.configured && (
                  <Button
                    variant="subtle"
                    color="red"
                    onClick={async () => {
                      try {
                        await clearSecret.mutateAsync({
                          environmentName: secret.name,
                          expectedRevision: profile.revision,
                        })
                      } catch (err) {
                        setError(getApiErrorMessage(err, 'Secret clear failed'))
                      }
                    }}
                  >
                    Clear
                  </Button>
                )}
              </Group>
            ))}
          </Stack>
        )}

        <TextInput
          label="Model argument name"
          description="Blank means the program owns its model: Antiphon never passes one, for the tier or for an exact model."
          value={modelArgumentName}
          onChange={(event) => setModelArgumentName(event.currentTarget.value)}
        />
        <Textarea
          label="Operator guidance"
          minRows={3}
          value={guidance}
          onChange={(event) => setGuidance(event.currentTarget.value)}
        />

        {selectedRunner && (
          <Alert color="gray" title={`${selectedRunner.displayName} setup`}>
            <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
              {selectedRunner.guidance || selectedRunner.description}
            </Text>
            <Group gap={4} mt="xs">
              {selectedRunner.capabilities.map((capability) => (
                <Badge
                  key={capability.name}
                  color={
                    capability.state === 'Supported'
                      ? 'green'
                      : capability.state === 'Degraded'
                        ? 'yellow'
                        : 'gray'
                  }
                  variant="light"
                >
                  {capability.name}: {capability.state}
                </Badge>
              ))}
            </Group>
          </Alert>
        )}

        <Alert color="blue" title="Command preview (no secrets)">
          <Text size="sm" ff="monospace">
            {commandPreview}
          </Text>
        </Alert>

        {profile && (
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              Models
            </Text>
            <Group gap={4}>
              {profile.models.map((model) => (
                <Badge
                  key={model.identifier}
                  color={
                    model.availability === 'Verified'
                      ? 'green'
                      : model.availability === 'Stale'
                        ? 'yellow'
                        : 'gray'
                  }
                  variant="light"
                >
                  {model.displayName} · {model.source}/{model.availability}
                </Badge>
              ))}
            </Group>
          </Stack>
        )}

        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={() => void save()}
            loading={createMutation.isPending || updateMutation.isPending}
          >
            Save
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
