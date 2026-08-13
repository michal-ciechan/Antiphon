import { useState } from 'react'
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  Loader,
  Stack,
  Table,
  Text,
  Tooltip,
} from '@mantine/core'
import {
  TbAlertCircle,
  TbCopy,
  TbEdit,
  TbPlayerPlay,
  TbPlus,
  TbRefresh,
  TbTrash,
} from 'react-icons/tb'
import {
  useAgentTuiProfiles,
  useDeleteAgentTuiProfile,
  useDuplicateAgentTuiProfile,
  useRefreshAgentTuiModels,
  useValidateAgentTuiProfile,
  type AgentTuiProfileDto,
} from '../../api/agentTui'
import { getApiErrorMessage } from '../../api/client'
import { AgentTuiProfileModal } from './AgentTuiProfileModal'

function validationColor(status: string) {
  switch (status) {
    case 'Succeeded':
      return 'green'
    case 'Partial':
      return 'yellow'
    case 'Failed':
    case 'TimedOut':
      return 'red'
    case 'Running':
      return 'blue'
    default:
      return 'gray'
  }
}

export function AgentTuiConfig() {
  const { data: profiles, isLoading, error } = useAgentTuiProfiles()
  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState<AgentTuiProfileDto | null>(null)
  const deleteMutation = useDeleteAgentTuiProfile()
  const duplicateMutation = useDuplicateAgentTuiProfile()
  const [actionError, setActionError] = useState<string | null>(null)

  if (isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading AI Agent TUI profiles">
        {getApiErrorMessage(error, 'Failed to load profiles')}
      </Alert>
    )
  }

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          Configure terminal runners (Claude Code, Codex, OpenCode), authentication, models, and
          validation without editing server files.
        </Text>
        <Button
          leftSection={<TbPlus size={16} />}
          onClick={() => {
            setEditing(null)
            setModalOpen(true)
          }}
        >
          New profile
        </Button>
      </Group>

      {actionError && (
        <Alert color="red" icon={<TbAlertCircle />} onClose={() => setActionError(null)} withCloseButton>
          {actionError}
        </Alert>
      )}

      <Table striped highlightOnHover withTableBorder>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Name</Table.Th>
            <Table.Th>Runner</Table.Th>
            <Table.Th>Auth</Table.Th>
            <Table.Th>Validation</Table.Th>
            <Table.Th>Flags</Table.Th>
            <Table.Th w={160}>Actions</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {(profiles ?? []).map((profile) => (
            <ProfileRow
              key={profile.id}
              profile={profile}
              onEdit={() => {
                setEditing(profile)
                setModalOpen(true)
              }}
              onDuplicate={async () => {
                try {
                  await duplicateMutation.mutateAsync({
                    profileId: profile.id,
                    displayName: `${profile.displayName} copy`,
                  })
                } catch (err) {
                  setActionError(getApiErrorMessage(err, 'Duplicate failed'))
                }
              }}
              onDelete={async () => {
                try {
                  await deleteMutation.mutateAsync(profile.id)
                } catch (err) {
                  setActionError(getApiErrorMessage(err, 'Delete failed'))
                }
              }}
              onError={setActionError}
            />
          ))}
        </Table.Tbody>
      </Table>

      <AgentTuiProfileModal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        profile={editing}
      />
    </Stack>
  )
}

function ProfileRow({
  profile,
  onEdit,
  onDuplicate,
  onDelete,
  onError,
}: {
  profile: AgentTuiProfileDto
  onEdit: () => void
  onDuplicate: () => Promise<void>
  onDelete: () => Promise<void>
  onError: (message: string) => void
}) {
  const refresh = useRefreshAgentTuiModels(profile.id)
  const validate = useValidateAgentTuiProfile(profile.id)

  return (
    <Table.Tr>
      <Table.Td>
        <Text fw={500}>{profile.displayName}</Text>
        <Text size="xs" c="dimmed" lineClamp={1}>
          {profile.commandPreview.executable} {profile.commandPreview.arguments.join(' ')}
        </Text>
      </Table.Td>
      <Table.Td>{profile.kind}</Table.Td>
      <Table.Td>
        <Badge variant="light">
          {profile.revisionDetails.authenticationMode === 'WrapperManaged'
            ? 'Wrapper'
            : 'Managed secrets'}
        </Badge>
      </Table.Td>
      <Table.Td>
        <Badge color={validationColor(profile.validationSummary.status)} variant="light">
          {profile.validationSummary.status}
          {!profile.validationSummary.isCurrentRevision &&
            profile.validationSummary.status !== 'NeverRun' &&
            ' (stale)'}
        </Badge>
      </Table.Td>
      <Table.Td>
        <Group gap={4}>
          {profile.isDefault && <Badge size="sm">Default</Badge>}
          {!profile.isEnabled && (
            <Badge size="sm" color="gray">
              Disabled
            </Badge>
          )}
        </Group>
      </Table.Td>
      <Table.Td>
        <Group gap={4} wrap="nowrap">
          <Tooltip label="Edit">
            <ActionIcon variant="subtle" onClick={onEdit}>
              <TbEdit size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Validate">
            <ActionIcon
              variant="subtle"
              loading={validate.isPending}
              onClick={async () => {
                try {
                  await validate.mutateAsync()
                } catch (err) {
                  onError(getApiErrorMessage(err, 'Validation failed'))
                }
              }}
            >
              <TbPlayerPlay size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Refresh models">
            <ActionIcon
              variant="subtle"
              loading={refresh.isPending}
              onClick={async () => {
                try {
                  await refresh.mutateAsync()
                } catch (err) {
                  onError(getApiErrorMessage(err, 'Model refresh failed'))
                }
              }}
            >
              <TbRefresh size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Duplicate">
            <ActionIcon variant="subtle" onClick={() => void onDuplicate()}>
              <TbCopy size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Delete">
            <ActionIcon variant="subtle" color="red" onClick={() => void onDelete()}>
              <TbTrash size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Table.Td>
    </Table.Tr>
  )
}
