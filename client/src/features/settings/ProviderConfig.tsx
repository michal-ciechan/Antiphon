import { useState } from 'react'
import {
  Button,
  Group,
  Modal,
  Paper,
  Stack,
  Table,
  Text,
  TextInput,
  Select,
  Switch,
  Badge,
  ActionIcon,
  Tooltip,
  Alert,
  Loader,
  PasswordInput,
} from '@mantine/core'
import { TbPlus, TbEdit, TbTrash, TbAlertCircle, TbPlugConnected, TbCheck, TbX } from 'react-icons/tb'
import {
  useProviders,
  useCreateProvider,
  useUpdateProvider,
  useDeleteProvider,
  useTestProvider,
  type LlmProviderDto,
} from '../../api/settings'

const PROVIDER_TYPE_LABELS: Record<number, string> = {
  0: 'Anthropic',
  1: 'OpenAI',
  2: 'Ollama',
}

const PROVIDER_TYPE_OPTIONS = [
  { value: '0', label: 'Anthropic' },
  { value: '1', label: 'OpenAI' },
  { value: '2', label: 'Ollama' },
]

export function ProviderConfig() {
  const { data: providers, isLoading: providersLoading, error: providersError } = useProviders()

  if (providersLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (providersError) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading provider configuration">
        {providersError?.message}
      </Alert>
    )
  }

  return <ProviderList providers={providers ?? []} />
}

// --- Provider List ---

function ProviderList({ providers }: { providers: LlmProviderDto[] }) {
  const createMutation = useCreateProvider()
  const updateMutation = useUpdateProvider()
  const deleteMutation = useDeleteProvider()
  const testMutation = useTestProvider()

  const [editModalOpen, setEditModalOpen] = useState(false)
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [editingProvider, setEditingProvider] = useState<LlmProviderDto | null>(null)
  const [deletingProvider, setDeletingProvider] = useState<LlmProviderDto | null>(null)
  const [testResult, setTestResult] = useState<{ id: string; success: boolean; message: string } | null>(null)

  const [formName, setFormName] = useState('')
  const [formType, setFormType] = useState('0')
  const [formApiKey, setFormApiKey] = useState('')
  const [formBaseUrl, setFormBaseUrl] = useState('')
  const [formEnabled, setFormEnabled] = useState(true)
  const [formDefaultModel, setFormDefaultModel] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const openCreateModal = () => {
    setEditingProvider(null)
    setFormName('')
    setFormType('0')
    setFormApiKey('')
    setFormBaseUrl('')
    setFormEnabled(true)
    setFormDefaultModel('')
    setFormError(null)
    setEditModalOpen(true)
  }

  const openEditModal = (provider: LlmProviderDto) => {
    setEditingProvider(provider)
    setFormName(provider.name)
    setFormType(String(provider.providerType))
    setFormApiKey('')
    setFormBaseUrl(provider.baseUrl)
    setFormEnabled(provider.isEnabled)
    setFormDefaultModel(provider.defaultModel)
    setFormError(null)
    setEditModalOpen(true)
  }

  const openDeleteModal = (provider: LlmProviderDto) => {
    setDeletingProvider(provider)
    setDeleteModalOpen(true)
  }

  const handleSave = async () => {
    setFormError(null)
    try {
      if (editingProvider) {
        await updateMutation.mutateAsync({
          id: editingProvider.id,
          data: {
            name: formName,
            providerType: Number(formType),
            apiKey: formApiKey || undefined,
            baseUrl: formBaseUrl,
            isEnabled: formEnabled,
            defaultModel: formDefaultModel,
          },
        })
      } else {
        await createMutation.mutateAsync({
          name: formName,
          providerType: Number(formType),
          apiKey: formApiKey,
          baseUrl: formBaseUrl,
          isEnabled: formEnabled,
          defaultModel: formDefaultModel,
        })
      }
      setEditModalOpen(false)
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'body' in err) {
        const apiErr = err as { body: unknown }
        if (apiErr.body && typeof apiErr.body === 'object' && 'errors' in apiErr.body) {
          const errors = (apiErr.body as { errors: Record<string, string[]> }).errors
          const messages = Object.values(errors).flat().join('. ')
          setFormError(messages)
          return
        }
        if (apiErr.body && typeof apiErr.body === 'object' && 'detail' in apiErr.body) {
          setFormError((apiErr.body as { detail: string }).detail)
          return
        }
      }
      setFormError('An unexpected error occurred.')
    }
  }

  const handleDelete = async () => {
    if (!deletingProvider) return
    try {
      await deleteMutation.mutateAsync(deletingProvider.id)
      setDeleteModalOpen(false)
      setDeletingProvider(null)
    } catch {
      setDeleteModalOpen(false)
    }
  }

  const handleTest = async (id: string) => {
    setTestResult(null)
    try {
      const result = await testMutation.mutateAsync(id)
      setTestResult({ id, success: result.success, message: result.message })
    } catch {
      setTestResult({ id, success: false, message: 'Failed to test connectivity.' })
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending

  return (
    <Stack>
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          Configure LLM providers for AI agent execution. API keys are stored securely on the server
          and never sent to the browser.
        </Text>
        <Button leftSection={<TbPlus />} onClick={openCreateModal}>
          Add Provider
        </Button>
      </Group>

      {providers.length > 0 ? (
        <Paper withBorder>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Type</Table.Th>
                <Table.Th>Default Model</Table.Th>
                <Table.Th>URL</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>API Key</Table.Th>
                <Table.Th w={160}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {providers.map((provider) => (
                <Table.Tr key={provider.id}>
                  <Table.Td fw={500}>{provider.name}</Table.Td>
                  <Table.Td>
                    <Badge variant="light" size="sm">
                      {PROVIDER_TYPE_LABELS[provider.providerType] ?? 'Unknown'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {provider.defaultModel || '--'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed" lineClamp={1} maw={220} title={provider.baseUrl}>
                      {provider.baseUrl || '--'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {provider.isEnabled ? (
                      <Badge variant="light" color="green" size="sm">
                        Enabled
                      </Badge>
                    ) : (
                      <Badge variant="light" color="gray" size="sm">
                        Disabled
                      </Badge>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {provider.apiKeyMasked || '--'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label="Test Connection">
                        <ActionIcon
                          variant="subtle"
                          color="blue"
                          loading={testMutation.isPending && testMutation.variables === provider.id}
                          onClick={() => handleTest(provider.id)}
                        >
                          <TbPlugConnected />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Edit">
                        <ActionIcon variant="subtle" onClick={() => openEditModal(provider)}>
                          <TbEdit />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Delete">
                        <ActionIcon
                          variant="subtle"
                          color="red"
                          onClick={() => openDeleteModal(provider)}
                        >
                          <TbTrash />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      ) : (
        <Paper withBorder p="xl">
          <Text ta="center" c="dimmed">
            No providers configured. Click "Add Provider" to get started.
          </Text>
        </Paper>
      )}

      {testResult && (
        <Alert
          color={testResult.success ? 'green' : 'red'}
          icon={testResult.success ? <TbCheck /> : <TbX />}
          title={testResult.success ? 'Connection Successful' : 'Connection Failed'}
          withCloseButton
          onClose={() => setTestResult(null)}
        >
          {testResult.message}
        </Alert>
      )}

      {/* Create / Edit Provider Modal */}
      <Modal
        opened={editModalOpen}
        onClose={() => setEditModalOpen(false)}
        title={editingProvider ? 'Edit Provider' : 'Add Provider'}
        size="md"
      >
        <Stack>
          {formError && (
            <Alert color="red" icon={<TbAlertCircle />}>
              {formError}
            </Alert>
          )}
          <TextInput
            label="Name"
            placeholder="My Anthropic Provider"
            required
            value={formName}
            onChange={(e) => setFormName(e.currentTarget.value)}
          />
          <Select
            label="Provider Type"
            required
            data={PROVIDER_TYPE_OPTIONS}
            value={formType}
            onChange={(v) => setFormType(v ?? '0')}
          />
          <PasswordInput
            label="API Key"
            placeholder={editingProvider ? 'Leave blank to keep current key' : 'sk-...'}
            description={editingProvider ? 'Leave blank to keep the existing API key.' : undefined}
            value={formApiKey}
            onChange={(e) => setFormApiKey(e.currentTarget.value)}
          />
          <TextInput
            label="Base URL"
            placeholder="https://api.anthropic.com"
            description="Leave blank to use the default URL for this provider."
            value={formBaseUrl}
            onChange={(e) => setFormBaseUrl(e.currentTarget.value)}
          />
          <TextInput
            label="Default Model"
            placeholder="claude-sonnet-4-20250514"
            value={formDefaultModel}
            onChange={(e) => setFormDefaultModel(e.currentTarget.value)}
          />
          <Switch
            label="Enabled"
            checked={formEnabled}
            onChange={(e) => setFormEnabled(e.currentTarget.checked)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setEditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} loading={isSaving}>
              {editingProvider ? 'Save Changes' : 'Create Provider'}
            </Button>
          </Group>
        </Stack>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        opened={deleteModalOpen}
        onClose={() => setDeleteModalOpen(false)}
        title="Delete Provider"
        size="sm"
      >
        <Stack>
          <Text>
            Are you sure you want to delete the provider "{deletingProvider?.name}"? This will also
            remove any model routing rules associated with this provider.
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setDeleteModalOpen(false)}>
              Cancel
            </Button>
            <Button color="red" onClick={handleDelete} loading={deleteMutation.isPending}>
              Delete
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}

