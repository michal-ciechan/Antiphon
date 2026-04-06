import { useState } from 'react'
import {
  Button,
  Group,
  Modal,
  Paper,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
  Badge,
  ActionIcon,
  Tooltip,
  Alert,
  Loader,
  Collapse,
  Divider,
  Select,
} from '@mantine/core'
import {
  TbPlus,
  TbEdit,
  TbTrash,
  TbAlertCircle,
  TbChevronDown,
  TbChevronRight,
} from 'react-icons/tb'
import {
  useTemplates,
  useCreateTemplate,
  useUpdateTemplate,
  useDeleteTemplate,
  useModelRoutingsByTemplate,
  useCreateModelRouting,
  useUpdateModelRouting,
  useDeleteModelRouting,
  useProviders,
  useTemplateGroups,
  useCreateTemplateGroup,
  useUpdateTemplateGroup,
  useDeleteTemplateGroup,
  type WorkflowTemplateDto,
  type ModelRoutingDto,
  type LlmProviderDto,
  type TemplateGroupDto,
} from '../../api/settings'

const PROVIDER_TYPE_LABELS: Record<number, string> = {
  0: 'Anthropic',
  1: 'OpenAI',
  2: 'Ollama',
}

export function TemplateManager() {
  const { data: templates, isLoading, error } = useTemplates()
  const { data: providers } = useProviders()
  const { data: templateGroups } = useTemplateGroups()
  const createMutation = useCreateTemplate()
  const updateMutation = useUpdateTemplate()
  const deleteMutation = useDeleteTemplate()

  const [editModalOpen, setEditModalOpen] = useState(false)
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [editingTemplate, setEditingTemplate] = useState<WorkflowTemplateDto | null>(null)
  const [deletingTemplate, setDeletingTemplate] = useState<WorkflowTemplateDto | null>(null)
  const [expandedTemplateId, setExpandedTemplateId] = useState<string | null>(null)

  const [formName, setFormName] = useState('')
  const [formDescription, setFormDescription] = useState('')
  const [formYaml, setFormYaml] = useState('')
  const [formGroupId, setFormGroupId] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const openCreateModal = () => {
    setEditingTemplate(null)
    setFormName('')
    setFormDescription('')
    setFormYaml('')
    setFormGroupId(null)
    setFormError(null)
    setEditModalOpen(true)
  }

  const openEditModal = (template: WorkflowTemplateDto) => {
    setEditingTemplate(template)
    setFormName(template.name)
    setFormDescription(template.description)
    setFormYaml(template.yamlDefinition)
    setFormGroupId(template.templateGroupId ?? null)
    setFormError(null)
    setEditModalOpen(true)
  }

  const openDeleteModal = (template: WorkflowTemplateDto) => {
    setDeletingTemplate(template)
    setDeleteModalOpen(true)
  }

  const handleSave = async () => {
    setFormError(null)
    const data = {
      name: formName,
      description: formDescription,
      yamlDefinition: formYaml,
      templateGroupId: formGroupId ?? null,
    }

    try {
      if (editingTemplate) {
        await updateMutation.mutateAsync({ id: editingTemplate.id, data })
      } else {
        await createMutation.mutateAsync(data)
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
    if (!deletingTemplate) return
    try {
      await deleteMutation.mutateAsync(deletingTemplate.id)
      setDeleteModalOpen(false)
      setDeletingTemplate(null)
    } catch {
      setDeleteModalOpen(false)
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending

  // Sort templates: built-in first, then grouped by group name, then ungrouped
  const sortedTemplates = [...(templates ?? [])].sort((a, b) => {
    if (a.isBuiltIn && !b.isBuiltIn) return -1
    if (!a.isBuiltIn && b.isBuiltIn) return 1
    const groupA = a.templateGroupName ?? ''
    const groupB = b.templateGroupName ?? ''
    if (groupA !== groupB) return groupA.localeCompare(groupB)
    return a.name.localeCompare(b.name)
  })

  const groupOptions = (templateGroups ?? []).map((g) => ({
    value: g.id,
    label: g.name,
  }))

  if (isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading templates">
        {error.message}
      </Alert>
    )
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          Manage workflow templates that define stage progression, executor types, and gate
          configuration.
        </Text>
        <Button leftSection={<TbPlus />} onClick={openCreateModal}>
          Add Template
        </Button>
      </Group>

      {sortedTemplates.length > 0 ? (
        <Stack gap="sm">
          {sortedTemplates.map((template) => (
            <Paper key={template.id} withBorder>
              <Table striped={false} highlightOnHover={false}>
                <Table.Tbody>
                  <Table.Tr>
                    <Table.Td fw={500} style={{ width: '25%' }}>
                      {template.name}
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed" lineClamp={2}>
                        {template.description}
                      </Text>
                    </Table.Td>
                    <Table.Td style={{ width: '120px' }}>
                      <Text size="sm" c="dimmed">
                        {template.templateGroupName ?? '—'}
                      </Text>
                    </Table.Td>
                    <Table.Td style={{ width: '80px' }}>
                      {template.isBuiltIn ? (
                        <Badge variant="light" color="blue" size="sm">
                          Built-in
                        </Badge>
                      ) : (
                        <Badge variant="light" color="gray" size="sm">
                          Custom
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td style={{ width: '120px' }}>
                      <Group gap="xs">
                        <Tooltip
                          label={
                            expandedTemplateId === template.id
                              ? 'Hide model routing'
                              : 'Model routing'
                          }
                        >
                          <ActionIcon
                            variant="subtle"
                            color="teal"
                            onClick={() =>
                              setExpandedTemplateId(
                                expandedTemplateId === template.id ? null : template.id
                              )
                            }
                          >
                            {expandedTemplateId === template.id ? (
                              <TbChevronDown />
                            ) : (
                              <TbChevronRight />
                            )}
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label="Edit">
                          <ActionIcon variant="subtle" onClick={() => openEditModal(template)}>
                            <TbEdit />
                          </ActionIcon>
                        </Tooltip>
                        {!template.isBuiltIn && (
                          <Tooltip label="Delete">
                            <ActionIcon
                              variant="subtle"
                              color="red"
                              onClick={() => openDeleteModal(template)}
                            >
                              <TbTrash />
                            </ActionIcon>
                          </Tooltip>
                        )}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                </Table.Tbody>
              </Table>

              <Collapse in={expandedTemplateId === template.id}>
                <Divider />
                <TemplateModelRouting templateId={template.id} providers={providers ?? []} />
              </Collapse>
            </Paper>
          ))}
        </Stack>
      ) : (
        <Paper withBorder p="xl">
          <Text ta="center" c="dimmed">
            No templates found. Click "Add Template" to create one.
          </Text>
        </Paper>
      )}

      {/* Template Groups Section */}
      <Divider mt="md" />
      <TemplateGroupsManager groups={templateGroups ?? []} />

      {/* Create / Edit Modal */}
      <Modal
        opened={editModalOpen}
        onClose={() => setEditModalOpen(false)}
        title={editingTemplate ? 'Edit Template' : 'Add Template'}
        size="lg"
      >
        <Stack>
          {formError && (
            <Alert color="red" icon={<TbAlertCircle />}>
              {formError}
            </Alert>
          )}
          <TextInput
            label="Name"
            placeholder="My Workflow Template"
            required
            value={formName}
            onChange={(e) => setFormName(e.currentTarget.value)}
          />
          <Textarea
            label="Description"
            placeholder="Describe what this workflow template does..."
            rows={2}
            value={formDescription}
            onChange={(e) => setFormDescription(e.currentTarget.value)}
          />
          <Select
            label="Group"
            placeholder="No group (ungrouped)"
            data={groupOptions}
            value={formGroupId}
            onChange={setFormGroupId}
            clearable
          />
          <Textarea
            label="YAML Definition"
            placeholder={`stages:\n  - name: my-stage\n    executorType: ai-agent\n    modelRouting: opus`}
            required
            rows={14}
            autosize
            minRows={10}
            maxRows={24}
            styles={{ input: { fontFamily: 'monospace', fontSize: '13px' } }}
            value={formYaml}
            onChange={(e) => setFormYaml(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setEditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} loading={isSaving}>
              {editingTemplate ? 'Save Changes' : 'Create Template'}
            </Button>
          </Group>
        </Stack>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        opened={deleteModalOpen}
        onClose={() => setDeleteModalOpen(false)}
        title="Delete Template"
        size="sm"
      >
        <Stack>
          <Text>
            Are you sure you want to delete the template "{deletingTemplate?.name}"? This action
            cannot be undone.
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

// --- Template Groups Manager ---

function TemplateGroupsManager({ groups }: { groups: TemplateGroupDto[] }) {
  const createMutation = useCreateTemplateGroup()
  const updateMutation = useUpdateTemplateGroup()
  const deleteMutation = useDeleteTemplateGroup()

  const [editModalOpen, setEditModalOpen] = useState(false)
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [editingGroup, setEditingGroup] = useState<TemplateGroupDto | null>(null)
  const [deletingGroup, setDeletingGroup] = useState<TemplateGroupDto | null>(null)

  const [formName, setFormName] = useState('')
  const [formDescription, setFormDescription] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const openCreateModal = () => {
    setEditingGroup(null)
    setFormName('')
    setFormDescription('')
    setFormError(null)
    setEditModalOpen(true)
  }

  const openEditModal = (group: TemplateGroupDto) => {
    setEditingGroup(group)
    setFormName(group.name)
    setFormDescription(group.description)
    setFormError(null)
    setEditModalOpen(true)
  }

  const openDeleteModal = (group: TemplateGroupDto) => {
    setDeletingGroup(group)
    setDeleteModalOpen(true)
  }

  const handleSave = async () => {
    setFormError(null)
    const data = { name: formName, description: formDescription }
    try {
      if (editingGroup) {
        await updateMutation.mutateAsync({ id: editingGroup.id, data })
      } else {
        await createMutation.mutateAsync(data)
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
    if (!deletingGroup) return
    try {
      await deleteMutation.mutateAsync(deletingGroup.id)
      setDeleteModalOpen(false)
      setDeletingGroup(null)
    } catch {
      setDeleteModalOpen(false)
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending

  return (
    <Stack>
      <Group justify="space-between">
        <Text fw={500} size="sm">
          Template Groups
        </Text>
        <Button size="xs" variant="light" leftSection={<TbPlus size={14} />} onClick={openCreateModal}>
          Add Group
        </Button>
      </Group>

      {groups.length > 0 ? (
        <Table striped highlightOnHover fz="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Description</Table.Th>
              <Table.Th w={80}>Templates</Table.Th>
              <Table.Th w={80}>Type</Table.Th>
              <Table.Th w={80}>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {groups.map((group) => (
              <Table.Tr key={group.id}>
                <Table.Td fw={500}>{group.name}</Table.Td>
                <Table.Td>
                  <Text size="sm" c="dimmed">
                    {group.description || '—'}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm" c="dimmed">
                    {group.templateCount}
                  </Text>
                </Table.Td>
                <Table.Td>
                  {group.isBuiltIn ? (
                    <Badge variant="light" color="blue" size="sm">
                      Built-in
                    </Badge>
                  ) : (
                    <Badge variant="light" color="gray" size="sm">
                      Custom
                    </Badge>
                  )}
                </Table.Td>
                <Table.Td>
                  {!group.isBuiltIn && (
                    <Group gap="xs">
                      <Tooltip label="Edit">
                        <ActionIcon size="sm" variant="subtle" onClick={() => openEditModal(group)}>
                          <TbEdit size={14} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Delete">
                        <ActionIcon
                          size="sm"
                          variant="subtle"
                          color="red"
                          loading={
                            deleteMutation.isPending && deleteMutation.variables === group.id
                          }
                          onClick={() => openDeleteModal(group)}
                        >
                          <TbTrash size={14} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      ) : (
        <Paper withBorder p="md">
          <Text ta="center" c="dimmed" size="sm">
            No template groups. Click "Add Group" to create one.
          </Text>
        </Paper>
      )}

      {/* Create / Edit Group Modal */}
      <Modal
        opened={editModalOpen}
        onClose={() => setEditModalOpen(false)}
        title={editingGroup ? 'Edit Template Group' : 'Add Template Group'}
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
            placeholder="e.g. Sprint Workflows"
            required
            value={formName}
            onChange={(e) => setFormName(e.currentTarget.value)}
          />
          <Textarea
            label="Description"
            placeholder="Describe this group of templates..."
            rows={2}
            value={formDescription}
            onChange={(e) => setFormDescription(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setEditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} loading={isSaving}>
              {editingGroup ? 'Save Changes' : 'Create Group'}
            </Button>
          </Group>
        </Stack>
      </Modal>

      {/* Delete Group Confirmation Modal */}
      <Modal
        opened={deleteModalOpen}
        onClose={() => setDeleteModalOpen(false)}
        title="Delete Template Group"
        size="sm"
      >
        <Stack>
          <Text>
            Are you sure you want to delete the group "{deletingGroup?.name}"? Templates in this
            group will become ungrouped.
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

// --- Per-template Model Routing ---

function TemplateModelRouting({
  templateId,
  providers,
}: {
  templateId: string
  providers: LlmProviderDto[]
}) {
  const { data: routings, isLoading } = useModelRoutingsByTemplate(templateId)
  const createMutation = useCreateModelRouting(templateId)
  const updateMutation = useUpdateModelRouting(templateId)
  const deleteMutation = useDeleteModelRouting(templateId)

  const [editModalOpen, setEditModalOpen] = useState(false)
  const [editingRouting, setEditingRouting] = useState<ModelRoutingDto | null>(null)

  const [formStageName, setFormStageName] = useState('')
  const [formModelName, setFormModelName] = useState('')
  const [formProviderId, setFormProviderId] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const providerOptions = providers.map((p) => ({
    value: p.id,
    label: `${p.name} (${PROVIDER_TYPE_LABELS[p.providerType] ?? 'Unknown'})`,
  }))

  const openCreateModal = () => {
    setEditingRouting(null)
    setFormStageName('')
    setFormModelName('')
    setFormProviderId(providers[0]?.id ?? '')
    setFormError(null)
    setEditModalOpen(true)
  }

  const openEditModal = (routing: ModelRoutingDto) => {
    setEditingRouting(routing)
    setFormStageName(routing.stageName)
    setFormModelName(routing.modelName)
    setFormProviderId(routing.providerId)
    setFormError(null)
    setEditModalOpen(true)
  }

  const handleSave = async () => {
    setFormError(null)
    try {
      if (editingRouting) {
        await updateMutation.mutateAsync({
          id: editingRouting.id,
          data: {
            stageName: formStageName,
            modelName: formModelName,
            providerId: formProviderId,
          },
        })
      } else {
        await createMutation.mutateAsync({
          stageName: formStageName,
          modelName: formModelName,
          providerId: formProviderId,
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

  const isSaving = createMutation.isPending || updateMutation.isPending

  return (
    <Stack p="md" gap="sm">
      <Group justify="space-between">
        <Text size="sm" fw={500}>
          Model Routing
        </Text>
        <Button
          size="xs"
          variant="light"
          leftSection={<TbPlus size={14} />}
          onClick={openCreateModal}
          disabled={providers.length === 0}
        >
          Add Rule
        </Button>
      </Group>

      {isLoading ? (
        <Loader size="xs" />
      ) : routings && routings.length > 0 ? (
        <Table striped highlightOnHover fz="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Stage</Table.Th>
              <Table.Th>Model</Table.Th>
              <Table.Th>Provider</Table.Th>
              <Table.Th w={80}>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {routings.map((routing) => {
              const provider = providers.find((p) => p.id === routing.providerId)
              return (
                <Table.Tr key={routing.id}>
                  <Table.Td fw={500}>{routing.stageName}</Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {routing.modelName}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {provider?.name ?? 'Unknown'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label="Edit">
                        <ActionIcon size="sm" variant="subtle" onClick={() => openEditModal(routing)}>
                          <TbEdit size={14} />
                        </ActionIcon>
                      </Tooltip>
                      <Tooltip label="Delete">
                        <ActionIcon
                          size="sm"
                          variant="subtle"
                          color="red"
                          loading={
                            deleteMutation.isPending && deleteMutation.variables === routing.id
                          }
                          onClick={() => deleteMutation.mutate(routing.id)}
                        >
                          <TbTrash size={14} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              )
            })}
          </Table.Tbody>
        </Table>
      ) : (
        <Text size="sm" c="dimmed">
          {providers.length === 0
            ? 'Add a provider first to configure model routing.'
            : 'No routing rules. Click "Add Rule" to map stages to models.'}
        </Text>
      )}

      <Modal
        opened={editModalOpen}
        onClose={() => setEditModalOpen(false)}
        title={editingRouting ? 'Edit Routing Rule' : 'Add Routing Rule'}
        size="md"
      >
        <Stack>
          {formError && (
            <Alert color="red" icon={<TbAlertCircle />}>
              {formError}
            </Alert>
          )}
          <TextInput
            label="Stage Name"
            placeholder="architecture"
            required
            value={formStageName}
            onChange={(e) => setFormStageName(e.currentTarget.value)}
          />
          <TextInput
            label="Model Name"
            placeholder="claude-opus-4-20250514"
            required
            value={formModelName}
            onChange={(e) => setFormModelName(e.currentTarget.value)}
          />
          <Select
            label="Provider"
            required
            data={providerOptions}
            value={formProviderId}
            onChange={(v) => setFormProviderId(v ?? '')}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setEditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} loading={isSaving}>
              {editingRouting ? 'Save Changes' : 'Create Rule'}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
