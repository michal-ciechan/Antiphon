import {
  ActionIcon,
  Alert,
  Button,
  Group,
  Loader,
  Paper,
  PasswordInput,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbAlertCircle, TbPlus, TbTrash } from 'react-icons/tb'
import { useApiKeys, useDeleteApiKey, usePutApiKey } from '../../api/apiKeys'
import { getApiErrorMessage } from '../../api/client'

function formatUpdatedAt(value: string): string {
  return new Date(value).toLocaleString()
}

/**
 * Write-only API-key editor used by both installation Settings and a project's settings modal.
 * A project key only lists and edits its own scope; at launch it overrides an identically named
 * global key.
 */
export function ApiKeysSection({ projectId }: { projectId?: string }) {
  const keys = useApiKeys(projectId)
  const putKey = usePutApiKey(projectId)
  const deleteKey = useDeleteApiKey(projectId)
  const [newName, setNewName] = useState('')
  const [newValue, setNewValue] = useState('')
  const [replacementValues, setReplacementValues] = useState<Record<string, string>>({})

  const scope = projectId ? 'Project' : 'Global'

  const addKey = async () => {
    if (!newName.trim() || !newValue) return
    try {
      await putKey.mutateAsync({ name: newName.trim(), value: newValue })
      setNewName('')
      setNewValue('')
      notifications.show({ color: 'green', message: 'API key saved' })
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'API key save failed') })
    }
  }

  const replaceKey = async (id: string, name: string) => {
    const value = replacementValues[id]
    if (!value) return
    try {
      await putKey.mutateAsync({ name, value })
      setReplacementValues((current) => ({ ...current, [id]: '' }))
      notifications.show({ color: 'green', message: 'API key replaced' })
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'API key replace failed') })
    }
  }

  const removeKey = async (id: string) => {
    try {
      await deleteKey.mutateAsync(id)
      notifications.show({ color: 'green', message: 'API key deleted' })
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'API key delete failed') })
    }
  }

  return (
    <Stack gap="sm">
      <div>
        <Text fw={500}>API Keys</Text>
        <Text size="sm" c="dimmed">
          Named values agents reference as {'{{key:NAME}}'}; project keys override global keys at launch.
          Values are write-only and never appear here after saving. Profile secrets authenticate the runner
          program under a profile; API keys are named values agents reference by placeholder.
        </Text>
      </div>

      <Paper withBorder p="sm">
        <Stack gap="xs">
          <Text size="sm" fw={500}>
            Add {scope.toLowerCase()} API key
          </Text>
          <Group align="flex-end" grow>
            <TextInput
              label="Name"
              placeholder="anthropic-default"
              value={newName}
              onChange={(event) => setNewName(event.currentTarget.value)}
            />
            <PasswordInput
              label="Value (missing)"
              placeholder="Enter value"
              value={newValue}
              onChange={(event) => setNewValue(event.currentTarget.value)}
            />
            <Button
              leftSection={<TbPlus size={16} />}
              onClick={() => void addKey()}
              loading={putKey.isPending}
              disabled={!newName.trim() || !newValue}
            >
              Save key
            </Button>
          </Group>
        </Stack>
      </Paper>

      {keys.isLoading ? (
        <Group justify="center" py="md">
          <Loader size="sm" />
        </Group>
      ) : keys.error ? (
        <Alert color="red" icon={<TbAlertCircle size={16} />}>
          {getApiErrorMessage(keys.error, 'Failed to load API keys')}
        </Alert>
      ) : (keys.data ?? []).length === 0 ? (
        <Text size="sm" c="dimmed">
          No {scope.toLowerCase()} API keys configured.
        </Text>
      ) : (
        <Paper withBorder>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Scope</Table.Th>
                <Table.Th>Updated</Table.Th>
                <Table.Th>Replace value</Table.Th>
                <Table.Th w={52}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {(keys.data ?? []).map((key) => (
                <Table.Tr key={key.id}>
                  <Table.Td>{key.name} (configured)</Table.Td>
                  <Table.Td>{key.projectId ? 'Project' : 'Global'}</Table.Td>
                  <Table.Td>{formatUpdatedAt(key.updatedAt)}</Table.Td>
                  <Table.Td>
                    <Group align="flex-end" wrap="nowrap">
                      <PasswordInput
                        label={`Value for ${key.name} (configured)`}
                        aria-label={`Replacement value for ${key.name}`}
                        placeholder="Enter new value to replace"
                        value={replacementValues[key.id] ?? ''}
                        onChange={(event) => {
                          const value = event.currentTarget.value
                          setReplacementValues((current) => ({
                            ...current,
                            [key.id]: value,
                          }))
                        }}
                      />
                      <Button
                        variant="light"
                        onClick={() => void replaceKey(key.id, key.name)}
                        loading={putKey.isPending}
                        disabled={!replacementValues[key.id]}
                      >
                        Replace
                      </Button>
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Tooltip label={`Delete ${key.name}`}>
                      <ActionIcon
                        color="red"
                        variant="subtle"
                        aria-label={`Delete ${key.name}`}
                        loading={deleteKey.isPending}
                        onClick={() => void removeKey(key.id)}
                      >
                        <TbTrash size={16} />
                      </ActionIcon>
                    </Tooltip>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      )}
    </Stack>
  )
}
